using System;
using System.Drawing;

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

        VerifyAttemptHistory(result);
        VerifyLoopCountdown();
        VerifyMenuLayouts();
        VerifyMenuSignaturePadding();
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
