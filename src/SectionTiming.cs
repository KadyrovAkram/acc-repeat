using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

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
        if (!game.Find()) return;
        string original = store.Snapshot();
        try
        {
            var frame = game.ReadTelemetry(true);
            if (frame == null)
                throw new InvalidOperationException("Mark the timing line on track in solo Free Practice.");
            var pin = store.ActiveCheckpoint();
            if (pin != null && !SameTrack(pin.Track, frame.Track))
                throw new InvalidOperationException("Load the active save's track before marking the section.");

            var section = store.CurrentSection(frame.Track);
            if (start)
            {
                if (section != null) store.Settings.Sections.Remove(section);
                section = new Section { Checkpoint = store.Settings.ActiveCheckpoint, Track = frame.Track, Start = frame.Position };
                store.Settings.Sections.Add(section);
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
            store.Save();
            timer.Reset();
            DisableLoop("timing lines changed");
            ReportStatus(start ? "Timing start marked on " + frame.Track + ". Mark the exit with Ctrl+Shift+L." :
                "Timing finish marked. Reset and cross both timing lines to record a run.");
        }
        catch (Exception error)
        {
            store.Restore(original);
            ReportStatus("Could not mark section. " + error.Message);
        }
    }

    // Sets both markers for the active drill without driving to them. Moving the start
    // or the finish starts a new history, as it does when marking on track.
    private static void ApplyMarkers(string track, double start, double finish, string message)
    {
        start = WrapPosition(start);
        finish = WrapPosition(finish);
        var pin = store.ActiveCheckpoint();
        if (pin != null && !SameTrack(pin.Track, track))
            throw new InvalidOperationException("Load the active save's track before setting the section.");
        double span = SectionTimer.ForwardDistance(start, finish);
        if (span < 0.0002 || span >= 0.5)
            throw new InvalidOperationException("The finish must be ahead of the start and within half a lap.");

        var section = store.CurrentSection(track);
        if (section == null)
        {
            section = new Section { Checkpoint = store.Settings.ActiveCheckpoint, Track = track, Start = start };
            store.Settings.Sections.Add(section);
        }
        else if (section.Start != start)
        {
            section.Start = start;
            section.RecentAttempts.Clear();
            section.BestAttempt = null;
        }
        SetFinish(section, finish);
        store.Save();
        timer.Reset();
        DisableLoop("timing lines changed");
        ReportStatus(message);
    }

    private static double WrapPosition(double position)
    {
        position %= 1;
        return position < 0 ? position + 1 : position;
    }

    private static bool SameTrack(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintSections()
    {
        var sections = store.Settings.Sections.Where(section => section.Checkpoint == store.Settings.ActiveCheckpoint).ToArray();
        if (sections.Length == 0)
            Console.WriteLine("No timing lines for this section. In ACC, press Ctrl+Shift+K at the start and Ctrl+Shift+L at the exit.");
        foreach (var section in sections)
            Console.WriteLine("{0}: start {1:P2}, finish {2}", section.Track, section.Start,
                section.Finish.HasValue ? section.Finish.Value.ToString("P2") : "unmarked");
    }

    private static void PollSection()
    {
        double now = sectionClock.Elapsed.TotalSeconds;
        if (now < nextSample) return;
        nextSample = now + 0.025;
        if (!store.Settings.Sections.Any(section => section.Checkpoint == store.Settings.ActiveCheckpoint && section.Finish.HasValue) ||
            !game.Find())
        {
            timer.Reset();
            return;
        }
        var frame = game.ReadTelemetry(false, readTyres: true);
        var sectionOnTrack = frame == null ? null : store.CurrentSection(frame.Track);
        var limits = sectionOnTrack == null ? null : FindTrackLimits(sectionOnTrack.Track);
        var result = timer.Observe(sectionOnTrack, frame, limits);
        if (result == null) return;
        var boundary = timer.Boundary;
        if (boundary != null && boundary.CutMetres > 0)
        {
            ReportInvalid(sectionOnTrack, result, boundary);
            StartLoopCountdown(sectionOnTrack, "Invalid. Track limits.");
            return;
        }
        if (boundary != null)
            DiagnosticLog.Info(string.Format("{0} wheel check: {1:0.000} s, {2} tyre samples, max nearest tyre {3:0.00} m outside.",
                sectionOnTrack.Track, result.Seconds, boundary.Samples, boundary.MaxOutsideMetres));
        ReportAttempt(sectionOnTrack, result);
        StartLoopCountdown(sectionOnTrack, string.Format("{0:0.00} seconds.", result.Seconds));
    }

    internal sealed class TelemetryFrame
    {
        public string Track;
        public int Packet;
        public bool Paused;
        public double Position, Distance, Speed, Time;
        public bool HasCoordinates;
        public double X, Z;
        public PointF[] Tyres;
    }

    internal sealed class SectionTimer
    {
        private Section section;
        private TelemetryFrame previous;
        private double? startTime;
        private double entrySpeed;
        private double path;
        internal BoundaryTrace Boundary { get; private set; }

        // When the car crossed the start of the attempt in progress, in sectionClock seconds.
        public double? StartedAt { get { return startTime; } }

        public void Reset()
        {
            previous = null;
            startTime = null;
            path = 0;
            Boundary = null;
        }

        public static double ForwardDistance(double from, double to)
        {
            double delta = to - from;
            return delta < 0 ? delta + 1 : delta;
        }

        public Attempt Observe(Section active, TelemetryFrame current, TrackLimits limits = null)
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
            // Distance actually driven in this frame, from speed. ACC's own distance and lap
            // position follow the track, so they don't get shorter when the car cuts a corner.
            double driven = (before.Speed + current.Speed) / 7.2 * seconds;
            if (progress == 0 || progress >= 0.99999)
            {
                // A repeated or slightly regressed lap position can't cross a timing line,
                // but the car can still be moving between these samples.
                if (startTime.HasValue)
                {
                    path += driven;
                    ObserveBoundary(limits, before, current, 1);
                }
                return null;
            }

            double toStart = ForwardDistance(before.Position, active.Start);
            double toFinish = ForwardDistance(before.Position, active.Finish.Value);
            bool startCrossed = toStart > 0 && toStart <= progress;
            bool finishCrossed = toFinish > 0 && toFinish <= progress;
            double drivenFrom = 0;
            if (startCrossed && (!startTime.HasValue || toStart <= toFinish))
            {
                double fraction = toStart / progress;
                startTime = before.Time + seconds * fraction;
                entrySpeed = before.Speed + (current.Speed - before.Speed) * fraction;
                path = 0;
                drivenFrom = fraction;
                Boundary = limits == null ? null : new BoundaryTrace();
                ObserveBoundary(limits, before, current, fraction);
            }
            if (!startTime.HasValue) return null;
            if (!finishCrossed)
            {
                path += driven * (1 - drivenFrom);
                ObserveBoundary(limits, before, current, 1);
                return null;
            }

            double exitFraction = toFinish / progress;
            ObserveBoundary(limits, before, current, exitFraction);
            double duration = before.Time + seconds * exitFraction - startTime.Value;
            startTime = null;
            if (duration <= 0) return null;
            return new Attempt
            {
                Seconds = duration, EntrySpeed = entrySpeed,
                ExitSpeed = before.Speed + (current.Speed - before.Speed) * exitFraction,
                Path = path + driven * (exitFraction - drivenFrom)
            };
        }

        private void ObserveBoundary(TrackLimits limits, TelemetryFrame before, TelemetryFrame current, double fraction)
        {
            if (Boundary == null || limits == null) return;
            Boundary.Observe(limits, before.Time + (current.Time - before.Time) * fraction,
                before.Tyres, current.Tyres, fraction);
        }
    }
}
