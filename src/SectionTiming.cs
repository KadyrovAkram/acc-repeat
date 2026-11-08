using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;

internal static partial class RepeatCorner
{
    private static readonly SectionTimer timer = new SectionTimer();
    private static readonly Stopwatch sectionClock = Stopwatch.StartNew();
    private static double nextSample;

    public sealed class Section
    {
        public string Checkpoint, Track;
        public double Start;
        public double? Finish;
        public List<Attempt> RecentAttempts = new List<Attempt>();
        public Attempt BestAttempt;
    }

    private static void TryMarkSection(bool start)
    {
        if (!FindGame()) return;
        string original = Json.Serialize(settings);
        try
        {
            var frame = ReadTelemetry(true);
            if (frame == null)
                throw new InvalidOperationException("Mark the timing line on track in solo Free Practice.");
            var pin = ActiveCheckpoint();
            if (pin != null && !SameTrack(pin.Track, frame.Track))
                throw new InvalidOperationException("Load the active checkpoint's track before marking the section.");

            var section = CurrentSection(frame.Track);
            if (start)
            {
                if (section != null) settings.Sections.Remove(section);
                section = new Section { Checkpoint = settings.ActiveCheckpoint, Track = frame.Track, Start = frame.Position };
                settings.Sections.Add(section);
            }
            else
            {
                if (section == null)
                    throw new InvalidOperationException("Mark the start first with Ctrl+Shift+K.");
                double span = SectionTimer.ForwardDistance(section.Start, frame.Position);
                if (span < 0.0002 || span >= 0.5)
                    throw new InvalidOperationException("Mark the finish further ahead on this section.");
                SetFinish(section, frame.Position);
            }
            StoreSettings();
            timer.Reset();
            Console.WriteLine(start ? "Timing start marked on " + frame.Track + ". Mark the exit with Ctrl+Shift+L." :
                "Timing finish marked. Retry and cross both marks to record a run.");
        }
        catch (Exception error)
        {
            settings = Json.Deserialize<ToolSettings>(original);
            Console.WriteLine("Could not mark section: " + error.Message);
        }
    }

    private static bool SameTrack(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static Section CurrentSection(string track)
    {
        return settings.Sections.Find(section => section.Checkpoint == settings.ActiveCheckpoint && SameTrack(section.Track, track));
    }

    private static void PrintSections()
    {
        var sections = settings.Sections.Where(section => section.Checkpoint == settings.ActiveCheckpoint).ToArray();
        if (sections.Length == 0)
            Console.WriteLine("No markers for this drill. In ACC, press Ctrl+Shift+K at the start and Ctrl+Shift+L at the exit.");
        foreach (var section in sections)
            Console.WriteLine("{0}: start {1:P2}, finish {2}", section.Track, section.Start,
                section.Finish.HasValue ? section.Finish.Value.ToString("P2") : "unmarked");
    }

    private static void PollSection()
    {
        double now = sectionClock.Elapsed.TotalSeconds;
        if (now < nextSample) return;
        nextSample = now + 0.025;
        if (!settings.Sections.Any(section => section.Checkpoint == settings.ActiveCheckpoint && section.Finish.HasValue) ||
            !FindGame())
        {
            timer.Reset();
            return;
        }
        var frame = ReadTelemetry(false);
        var sectionOnTrack = frame == null ? null : CurrentSection(frame.Track);
        var result = timer.Observe(sectionOnTrack, frame);
        if (result != null)
            ReportAttempt(sectionOnTrack, result);
    }

    private static MemoryMappedViewAccessor OpenPage(string name, int size)
    {
        using (var map = MemoryMappedFile.OpenExisting(@"Local\acpmf_" + name, MemoryMappedFileRights.Read))
            return map.CreateViewAccessor(0, size, MemoryMappedFileAccess.Read);
    }

    private static TelemetryFrame ReadTelemetry(bool allowPaused)
    {
        try
        {
            // Field offsets are from Kunos's ACC Shared Memory v1.8.12 layout (4-byte packing).
            using (var graphics = OpenPage("graphics", 1240))
            using (var physics = OpenPage("physics", 32))
            using (var session = OpenPage("static", 688))
            {
                int status = graphics.ReadInt32(4);
                if ((status != 2 && !(allowPaused && status == 3)) || graphics.ReadInt32(8) != 0 ||
                    graphics.ReadInt32(160) != 0 || graphics.ReadInt32(1236) != 0 || session.ReadInt32(684) != 0)
                    return null;

                byte[] track = new byte[66];
                session.ReadArray(134, track, 0, track.Length);
                string trackName = Encoding.Unicode.GetString(track).Split('\0')[0];
                double position = graphics.ReadSingle(248);
                double distance = graphics.ReadSingle(156);
                double speed = physics.ReadSingle(28);
                if (trackName.Length == 0 || !(position >= 0 && position <= 1) ||
                    !(distance >= 0 && distance < double.PositiveInfinity) || !(speed >= 0 && speed < 500))
                    return null;
                return new TelemetryFrame
                {
                    Track = trackName, Position = position, Distance = distance, Speed = speed,
                    Packet = graphics.ReadInt32(0), Time = sectionClock.Elapsed.TotalSeconds
                };
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private sealed class TelemetryFrame
    {
        public string Track;
        public int Packet;
        public double Position, Distance, Speed, Time;
    }

    private sealed class SectionTimer
    {
        private Section section;
        private TelemetryFrame previous;
        private double? startTime;
        private double entrySpeed;

        public void Reset()
        {
            previous = null;
            startTime = null;
        }

        public static double ForwardDistance(double from, double to)
        {
            double delta = to - from;
            return delta < 0 ? delta + 1 : delta;
        }

        public Attempt Observe(Section active, TelemetryFrame current)
        {
            if (active != section || active == null || !active.Finish.HasValue || current == null)
                Reset();
            section = active;
            if (active == null || !active.Finish.HasValue || current == null)
                return null;

            var before = previous;
            if (before != null && current.Packet == before.Packet) return null;
            previous = current;
            if (before == null) return null;

            double progress = ForwardDistance(before.Position, current.Position);
            double seconds = current.Time - before.Time;
            double travel = current.Distance - before.Distance;
            double plausibleTravel = Math.Max(before.Speed, current.Speed) / 3.6 * seconds + 8;
            if (seconds <= 0 || seconds > 0.35 || travel < -1 || travel > plausibleTravel ||
                (progress > 0.025 && progress < 0.99999))
            {
                startTime = null;
                return null;
            }
            if (progress == 0 || progress >= 0.99999) return null;

            double toStart = ForwardDistance(before.Position, active.Start);
            double toFinish = ForwardDistance(before.Position, active.Finish.Value);
            bool startCrossed = toStart > 0 && toStart <= progress;
            if (startCrossed && (!startTime.HasValue || toStart <= toFinish))
            {
                double fraction = toStart / progress;
                startTime = before.Time + seconds * fraction;
                entrySpeed = before.Speed + (current.Speed - before.Speed) * fraction;
            }
            if (!startTime.HasValue || toFinish <= 0 || toFinish > progress) return null;

            double exitFraction = toFinish / progress;
            double duration = before.Time + seconds * exitFraction - startTime.Value;
            startTime = null;
            if (duration <= 0) return null;
            return new Attempt
            {
                Seconds = duration, EntrySpeed = entrySpeed,
                ExitSpeed = before.Speed + (current.Speed - before.Speed) * exitFraction
            };
        }
    }
}
