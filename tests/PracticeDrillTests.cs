using System;
using System.Drawing;
using System.IO;
using System.Linq;

internal static class PracticeDrillTests
{
    private static int Main()
    {
        RepeatCorner.VerifyPracticeDrills();
        Console.WriteLine("Practice drill checks passed.");
        return 0;
    }
}

internal static partial class RepeatCorner
{
    internal static void VerifyPracticeDrills()
    {
        var oldSave = new Save { Row = 1, Slot = "4", Created = "old", Track = "monza", Mode = "Practice" };
        var newer = new Save { Row = 0, Slot = "5", Created = "new", Track = "monza", Mode = "Practice" };
        var pin = new Checkpoint { Name = "Monza T1", Slot = "4", Created = "old", Track = "monza" };
        Require(SelectSave(new[] { newer, oldSave }, pin) == oldSave, "A newer save displaced the pin.");
        Require(SelectSave(new[] { newer, oldSave }, null) == newer, "Latest mode did not select the first save.");
        oldSave.Created = "replacement";
        try
        {
            SelectSave(new[] { newer, oldSave }, pin);
            throw new Exception("A replaced save was accepted as the pinned save.");
        }
        catch (InvalidOperationException) { }

        var persisted = new ToolSettings { ActiveCheckpoint = pin.Name, Wheel = new WheelBinding { Button = 31 } };
        persisted.Checkpoints.Add(pin);
        var roundTrip = Json.Deserialize<ToolSettings>(Json.Serialize(persisted));
        Require(roundTrip.Checkpoints[0].Created == pin.Created && roundTrip.Wheel.Button == 31,
            "Pin or wheel binding failed to round-trip.");
        Require(ButtonIndex(1u << 31) == 31, "Button 32 was decoded incorrectly.");

        var timer = new SectionTimer();
        var section = new Section { Track = "monza", Start = 0.9995, Finish = 0.0005 };
        timer.Observe(section, Frame(1, 0.999, 0, 0, 100));
        timer.Observe(section, Frame(2, 0.000, 0.1, 3, 120));
        var result = timer.Observe(section, Frame(3, 0.001, 0.2, 6, 140));
        Require(result != null && Math.Abs(result.Seconds - 0.1) < 0.000001 &&
            Math.Abs(result.EntrySpeed - 110) < 0.000001 && Math.Abs(result.ExitSpeed - 130) < 0.000001,
            "Crossing interpolation or lap wrap is incorrect.");
        // Half of each 0.1 s frame at 110 km/h and 130 km/h average speed.
        Require(Math.Abs(result.Path - (110 + 130) / 3.6 * 0.05) < 0.000001,
            "The driven distance between the markers is incorrect.");

        timer.Reset();
        var normal = new Section { Track = "monza", Start = 0.1, Finish = 0.102 };
        timer.Observe(normal, Frame(1, 0.099, 0, 0, 100));
        timer.Observe(normal, Frame(2, 0.101, 0.1, 3, 105));
        Require(timer.Observe(normal, Frame(3, 0.103, 0.2, 200, 120)) == null,
            "A teleport across the finish counted as an attempt.");

        timer.Reset();
        timer.Observe(normal, Frame(1, 0.099, 0, 0, 100));
        timer.Observe(normal, Frame(2, 0.101, 0.1, 3, 105));
        timer.Observe(normal, null);
        timer.Observe(normal, Frame(3, 0.101, 0.2, 3, 105));
        Require(timer.Observe(normal, Frame(4, 0.103, 0.3, 6, 120)) == null,
            "An interrupted run counted after resuming mid-section.");

        timer.Reset();
        var longer = new Section { Track = "monza", Start = 0.1, Finish = 0.106 };
        timer.Observe(longer, Frame(1, 0.099, 0.0, 0, 72));
        timer.Observe(longer, Frame(2, 0.101, 0.1, 2, 72));
        timer.Observe(longer, Frame(3, 0.103, 0.2, 4, 72));
        timer.Observe(longer, Frame(4, 0.105, 0.3, 6, 72));
        var steady = timer.Observe(longer, Frame(5, 0.107, 0.4, 8, 72));
        Require(steady != null && Math.Abs(steady.Path - 6) < 0.000001,
            "Driven distance was not summed across frames.");

        VerifyCutDetection();
        VerifyTrackMap();
        VerifyTrackMapImport();
        VerifyBundledTrackMaps();
        VerifyRunRemoval();
        VerifyAttemptHistory(result);
        VerifyCheckpointRename();
        VerifyLoopCountdown();
        VerifyMenuLayouts();
        VerifyMenuSignaturePadding();
    }

    private static void VerifyCheckpointRename()
    {
        settings = new ToolSettings { ActiveCheckpoint = "Monza T1" };
        var save = new Save { Slot = "4", Created = "old", Track = "monza", Mode = "Practice", Row = 1 };
        var replacement = new Save { Slot = "4", Created = "new", Track = "monza", Mode = "Practice" };
        var saves = new[] { replacement, save };
        settings.Checkpoints.Add(new Checkpoint { Name = "Monza", Slot = "4", Created = "old", Track = "monza" });
        settings.Checkpoints.Add(new Checkpoint { Name = "Monza T1", Slot = "4", Created = "old", Track = "monza" });
        var section = new Section { Checkpoint = "Monza", Track = "monza", Start = 0.04, Finish = 0.18 };
        var attempt = new Attempt { Seconds = 28.417 };
        RecordAttempt(section, attempt);
        settings.Sections.Add(section);

        CollapseDuplicatePins();
        Require(settings.Checkpoints.Count == 1 && ActiveCheckpoint().Name == "Monza T1" && CurrentSection("monza") == section,
            "Consolidating old names lost the active pin or its timing section.");

        SetCheckpointName(saves, save, "  First chicane  ");
        Require(settings.Checkpoints.Count == 1 && FindCheckpoint("Monza T1") == null &&
            ActiveCheckpoint().Name == "First chicane" && SelectSave(saves, ActiveCheckpoint()) == save &&
            CurrentSection("monza") == section && section.Start == 0.04 && section.Finish == 0.18 &&
            section.BestAttempt == attempt && section.RecentAttempts[0] == attempt,
            "Renaming a pin created an alias or lost the checkpoint's timing data.");
        SetCheckpointName(saves, save, "First Chicane");
        Require(settings.Checkpoints.Count == 1 && CurrentSection("monza") == section && settings.ActiveCheckpoint == "First Chicane",
            "A case-only rename did not update the active name and its timing section.");

        SetCheckpointName(saves, replacement, "New save");
        Require(settings.Checkpoints.Count == 2 && FindCheckpoint("First Chicane").Created == "old",
            "Pinning a new save in the same slot renamed an existing checkpoint.");
        try
        {
            SetCheckpointName(saves, replacement, "First Chicane");
            throw new Exception("A rename was allowed to take another save's name.");
        }
        catch (InvalidOperationException) { }

        settings.Checkpoints.Add(new Checkpoint { Name = "Separate drill", Slot = "4", Created = "old", Track = "monza" });
        settings.Sections.Add(new Section { Checkpoint = "Separate drill", Track = "monza", Start = 0.21, Finish = 0.28 });
        CollapseDuplicatePins();
        Require(settings.Checkpoints.Count == 3 && settings.Sections.Count == 2,
            "Consolidating old names removed a separate timing drill on the same save.");
    }

    private static void VerifyMenuLayouts()
    {
        var fullHd = MenuLayout.Select(new Size(1920, 1080));
        var quadHd = MenuLayout.Select(new Size(2560, 1440));
        var original = MenuLayout.Select(new Size(2880, 1800));
        Require(fullHd.ToClient(fullHd.QuitButton) == new Point(959, 768) &&
            quadHd.ToClient(quadHd.QuitButton) == new Point(1278, 1024),
            "The 16:9 pause controls are out of place.");
        Require(original.ToClient(original.SavedGamesButton) == new Point(1140, 1332),
            "The original layout's Saved Games button moved.");

        foreach (var candidate in new[] { fullHd, quadHd, original, MenuLayout.Select(new Size(1440, 900)) })
        {
            var client = new Rectangle(Point.Empty, candidate.ClientSize);
            for (int row = 0; row < VisibleSaveRows; row++)
            {
                int offset = candidate.RowOffset(row);
                var play = candidate.PlayButtonPosition;
                play.Offset(0, offset);
                Require(client.Contains(candidate.ToClient(play)) &&
                    client.Contains(candidate.CaptureRegion(candidate.SaveRow[1], offset)),
                    "A Saved Games row extends outside the ACC client area.");
            }
        }
    }

    private static void VerifyMenuSignaturePadding()
    {
        const int padding = 4;
        byte[] mask = {
            2, 2, 2, 2, 2,
            2, 1, 2, 1, 2,
            2, 1, 1, 1, 2,
            2, 1, 2, 1, 2,
            2, 2, 2, 2, 2,
        };
        var label = new Signature(0, 0, 5, 5, Convert.ToBase64String(mask));
        using (var frame = new Bitmap(label.Width + 2 * padding, label.Height + 2 * padding))
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.Black);
            Require(!label.Matches(frame, 0.70, padding), "A blank menu region matched a label.");
            graphics.Clear(Color.White);
            Require(!label.Matches(frame, 0.70, padding), "A solid bright region matched a label.");
            graphics.Clear(Color.Black);
            for (int y = 0; y < label.Height; y++)
            for (int x = 0; x < label.Width; x++)
                if (mask[y * label.Width + x] == 1)
                    frame.SetPixel(padding + x - 2, padding + y + 3, Color.White);
            Require(label.Matches(frame, 0.70, padding), "A slightly shifted label was rejected.");
        }
    }

    private static void VerifyLoopCountdown()
    {
        var pending = new LoopCountdown(10, 3);
        Require(!pending.Update(11, false).HasValue, "The countdown started while the result was speaking.");
        Require(pending.Update(12, true) == 3, "The countdown did not start when the result finished.");
        Require(!pending.Update(12.5, true).HasValue, "A countdown number was announced twice.");
        Require(pending.Update(13, true) == 2 && pending.Update(14, true) == 1,
            "The countdown did not report the remaining seconds.");
        Require(pending.Update(15, true) == 0 && !pending.Update(16, true).HasValue,
            "The countdown did not trigger exactly one retry.");

        var stalledVoice = new LoopCountdown(10, 2);
        Require(!stalledVoice.Update(15.9, false).HasValue && stalledVoice.Update(16, false) == 2 &&
            stalledVoice.Update(18, false) == 0, "A stalled voice could block the countdown.");

        var focusPause = new LoopCountdown(10, 3);
        Require(focusPause.Update(10, true) == 3, "The initial countdown did not start.");
        Require(focusPause.Suspend(11.5) && !focusPause.Suspend(15) &&
            !focusPause.Update(25, true).HasValue, "Time away advanced or restarted the suspended countdown.");
        Require(focusPause.Resume(25) && focusPause.Update(25, true) == 2 &&
            focusPause.Update(25.75, true) == 1 && focusPause.Update(26.5, true) == 0,
            "Returning to ACC did not preserve the remaining countdown.");

        var calloutPause = new LoopCountdown(10, 3);
        calloutPause.Suspend(10.5);
        Require(!calloutPause.Update(100, true).HasValue, "A completed callout started a countdown while suspended.");
        calloutPause.Resume(100);
        Require(calloutPause.Update(100, true) == 3 && calloutPause.Update(103, true) == 0,
            "Suspending during the callout shortened the countdown.");

        loopSave = new Save { Slot = "4", Created = "selected", Track = "monza" };
        countdown = new LoopCountdown(10, 3);
        CancelLoopCountdown();
        Require(countdown == null && loopSave != null, "A manual retry did not cancel only the pending countdown.");
        countdown = new LoopCountdown(10, 3);
        DisableLoop("test cancellation");
        Require(countdown == null && loopSave == null, "Stopping the loop left an automatic retry armed.");
    }

    private static void VerifyTrackMap()
    {
        const double radius = 900;
        var map = new TrackMap { Track = "monza" };
        for (int sample = 0; sample < 3 * MapBins; sample++)
        {
            double position = (sample + 0.5) / (3.0 * MapBins), angle = position * 2 * Math.PI;
            map.Add(position, radius * Math.Cos(angle), radius * Math.Sin(angle));
        }
        var view = TrackMapView.From("monza", map);
        Require(view.Coverage == 1, "A full lap did not complete the map.");

        double gap;
        var picked = view.Nearest(radius * Math.Cos(0.5 * Math.PI) * 1.02, radius * Math.Sin(0.5 * Math.PI) * 1.02, out gap);
        Require(picked.HasValue && Math.Abs(picked.Value - 0.25) < 0.001 && Math.Abs(gap - radius * 0.02) < 1,
            "A click beside the track did not snap to the nearest lap position.");
        var point = view.PointAt(0.25).Value;
        Require(Math.Abs(point.X) < 5 && Math.Abs(point.Y - radius) < 5, "A lap position was drawn in the wrong place.");
        picked = view.Nearest(radius, -3, out gap);
        Require(picked.HasValue && (picked.Value < 0.001 || picked.Value > 0.999), "Picking across the start line failed.");

        var half = new TrackMap { Track = "monza" };
        for (int sample = 0; sample < MapBins / 2; sample++)
        {
            double position = (sample + 0.5) / MapBins, angle = position * 2 * Math.PI;
            half.Add(position, radius * Math.Cos(angle), radius * Math.Sin(angle));
        }
        view = TrackMapView.From("monza", half);
        picked = view.Nearest(radius * Math.Cos(1.5 * Math.PI), radius * Math.Sin(1.5 * Math.PI), out gap);
        Require(Math.Abs(view.Coverage - 0.5) < 0.000001 && picked.HasValue && picked.Value <= 0.5,
            "A half-drawn map picked a point it hasn't recorded.");

        var restored = Json.Deserialize<System.Collections.Generic.List<TrackMap>>(Json.Serialize(new[] { half }));
        Require(restored.Count == 1 && restored[0].Filled.Count(filled => filled) == MapBins / 2 &&
            restored[0].X[10] == half.X[10], "A track map failed to round-trip.");
    }

    private static void VerifyTrackMapImport()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "monza.bin");
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 0x72, 0x65, 0x74, 0x6d });
                writer.Write((ushort)1);
                writer.Write(0L);
                for (int sample = MapBins - 1; sample >= 0; sample--)
                {
                    float position = (sample + 0.25f) / MapBins;
                    double angle = position * 2 * Math.PI;
                    writer.Write((float)(900 * Math.Cos(angle)));
                    writer.Write((float)(900 * Math.Sin(angle)));
                    writer.Write(position);
                }
            }
            var map = ReadTrackMapFile("monza", path);
            var view = TrackMapView.From("monza", map);
            Require(map.Imported && view.Coverage == 1, "A complete import did not fill the lap.");
            foreach (double position in new[] { 0, 0.0405, 0.1807 })
            {
                var point = view.PointAt(position).Value;
                double gap;
                var picked = view.Nearest(point.X, point.Y, out gap);
                Require(picked.HasValue && Math.Min(Math.Abs(position - picked.Value), 1 - Math.Abs(position - picked.Value)) < 0.00001,
                    "An imported map point picked a different lap position.");
            }

            try
            {
                ReadTrackMapFile("spa", path);
                throw new Exception("The import accepted another circuit's map.");
            }
            catch (InvalidDataException) { }

            using (var stream = File.OpenWrite(path)) stream.SetLength(stream.Length - 12 * 50);
            try
            {
                ReadTrackMapFile("monza", path);
                throw new Exception("The import accepted a map with a large gap.");
            }
            catch (InvalidDataException) { }
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void VerifyBundledTrackMaps()
    {
        var files = Directory.GetFiles(BundledMapsPath, "*.bin");
        Require(files.Length == 25, "The build did not include all 25 track maps.");
        foreach (string file in files)
        {
            string track = Path.GetFileNameWithoutExtension(file);
            Require(MapView(track).Coverage == 1, "The bundled map did not load for " + track + ".");
        }

        var monza = MapView("monza");
        foreach (double position in new[] { 0.0405, 0.1807 })
        {
            var point = monza.PointAt(position).Value;
            double gap;
            var picked = monza.Nearest(point.X, point.Y, out gap);
            Require(picked.HasValue && Math.Abs(position - picked.Value) < 0.00001,
                "The bundled Monza map moved a timing marker.");
        }
        Require(MapView("nurburgring").PointAt(0.5) != MapView("nurburgring_24h").PointAt(0.5),
            "The Nürburgring layouts shared the same map.");
        Require(MapView("unmapped_track").IsEmpty, "An unknown track loaded a different circuit's map.");

        var recorded = new TrackMap { Track = "monza" };
        var custom = new TrackMap { Track = "monza", Imported = true };
        trackMaps.Add(recorded);
        try
        {
            Require(FindTrackMap("monza") != recorded, "A partial recording hid the bundled map.");
            trackMaps.Add(custom);
            Require(FindTrackMap("monza") == custom, "A bundled map hid the user's imported map.");
        }
        finally
        {
            trackMaps.Remove(custom);
            trackMaps.Remove(recorded);
        }
    }

    private static void VerifyRunRemoval()
    {
        // Two cuts recorded first, while there was nothing to compare against.
        var section = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };
        foreach (var run in new[]
        {
            new Attempt { Seconds = 28.9, Path = 811 }, new Attempt { Seconds = 28.7, Path = 812 },
            new Attempt { Seconds = 29.1, Path = 810 }, new Attempt { Seconds = 27.4, Path = 789 },
            new Attempt { Seconds = 27.6, Path = 791 }
        })
            RecordAttempt(section, run);
        Require(section.BestAttempt.Seconds == 27.4, "Setup: the cut should be the recorded best.");
        Require(PruneCuts(section) == 2 && section.RecentAttempts.Count == 3 && section.BestAttempt.Seconds == 28.7,
            "Earlier cut runs were not removed once the usual line was known.");
        Require(PruneCuts(section) == 0, "Clean runs were removed as cuts.");

        // A best restored from the settings file is a separate object with the same values.
        section.BestAttempt = Json.Deserialize<Attempt>(Json.Serialize(section.BestAttempt));
        Require(DropAttempt(section, section.RecentAttempts.Find(run => run.Seconds == 28.7)) &&
            section.BestAttempt.Seconds == 28.9 && section.RecentAttempts.Count == 2,
            "Removing the personal best did not promote the next quickest run.");
        Require(!DropAttempt(section, section.RecentAttempts.Find(run => run.Seconds == 29.1)) &&
            section.BestAttempt.Seconds == 28.9, "Removing a slower run changed the personal best.");
        try
        {
            DropAttempt(section, new Attempt { Seconds = 30 });
            throw new Exception("Removing a run that isn't in the history was accepted.");
        }
        catch (InvalidOperationException) { }

        var legacy = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };
        foreach (double seconds in new[] { 29.0, 27.0, 28.0, 28.5 }) RecordAttempt(legacy, new Attempt { Seconds = seconds });
        Require(PruneCuts(legacy) == 0 && legacy.RecentAttempts.Count == 4,
            "Runs without a measured distance were removed as cuts.");
    }

    private static void VerifyCutDetection()
    {
        var section = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };
        Require(!CutMetres(section, new Attempt { Seconds = 28, Path = 700 }).HasValue,
            "A run was called a cut with no earlier runs to compare against.");

        foreach (double path in new[] { 812.0, 809.0, 814.0 })
            section.RecentAttempts.Add(new Attempt { Seconds = 29, Path = path });
        section.RecentAttempts.Add(new Attempt { Seconds = 29 });
        Require(!CutMetres(section, new Attempt { Seconds = 28.9, Path = 806 }).HasValue,
            "A slightly tighter line was called a cut.");
        Require(!CutMetres(section, new Attempt { Seconds = 28.5, Path = 830 }).HasValue,
            "Running wide was called a cut.");
        var cut = CutMetres(section, new Attempt { Seconds = 27.4, Path = 790 });
        Require(cut.HasValue && Math.Abs(cut.Value - 22) < 0.000001, "A chicane cut was not detected.");
        Require(!CutMetres(section, new Attempt { Seconds = 27.4 }).HasValue,
            "A run without a measured distance was called a cut.");
    }

    private static void VerifyAttemptHistory(Attempt completedRun)
    {
        var legacy = Json.Deserialize<ToolSettings>(
            "{\"ActiveCheckpoint\":\"Monza T1\",\"Sections\":[{\"Checkpoint\":\"Monza T1\",\"Track\":\"monza\",\"Start\":0.04,\"Finish\":0.18}]}");
        var section = legacy.Sections[0];
        Require(section.RecentAttempts.Count == 0 && section.BestAttempt == null && section.Finish == 0.18,
            "A saved section without history did not upgrade cleanly.");
        Require(!RecordAttempt(section, completedRun).HasValue, "The first run had a previous-best delta.");

        for (int seconds = 11; seconds <= 16; seconds++)
            Require(RecordAttempt(section, new Attempt { Seconds = seconds }) > 0, "A slower run did not show a positive delta.");
        Require(section.RecentAttempts.Count == 5 && section.RecentAttempts[0].Seconds == 16 &&
            section.RecentAttempts[4].Seconds == 12 && section.BestAttempt == completedRun,
            "The rolling history did not retain the five latest runs and independent best.");

        var restored = Json.Deserialize<ToolSettings>(Json.Serialize(legacy)).Sections[0];
        Require(restored.RecentAttempts.Count == 5 && restored.BestAttempt.EntrySpeed == completedRun.EntrySpeed,
            "History did not persist through a settings round-trip.");
        Require(restored.RecentAttempts[0].PreviousBestDelta == 16 - completedRun.Seconds,
            "The latest attempt lost its comparison to the previous best.");
        var fasterRun = new Attempt { Seconds = completedRun.Seconds / 2 };
        Require(RecordAttempt(restored, fasterRun) < 0 && restored.BestAttempt == fasterRun,
            "An improved run did not replace the best or show a negative delta.");
        SetFinish(restored, 0.18);
        Require(restored.BestAttempt == fasterRun, "Reconfirming the same marker cleared history.");
        SetFinish(restored, 0.20);
        Require(restored.RecentAttempts.Count == 0 && restored.BestAttempt == null,
            "Times from a different marker pair carried over.");
    }

    private static TelemetryFrame Frame(int packet, double position, double time, double distance, double speed)
    {
        return new TelemetryFrame
        {
            Track = "monza", Packet = packet, Position = position, Time = time, Distance = distance, Speed = speed
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
