using System;
using System.Collections.Generic;

// Places the timing markers from one normal run through the corner: the start just
// before the driver brakes, and the finish once they have been flat out on the exit.
internal static partial class RepeatCorner
{
    private const double MarkerSearchSeconds = 120, MarkerNudgeStep = 0.001;
    private static MarkerFinder markerFinder;
    private static double markerSearchStarted, nextMarkerSample;

    private static bool FindingMarkers { get { return markerFinder != null; } }

    // Engine thread: toggled from the desk or the console.
    private static void ToggleMarkerSearch()
    {
        if (markerFinder != null)
        {
            markerFinder = null;
            ReportStatus("Marker search cancelled.");
            return;
        }
        DisableLoop("finding new markers");
        markerFinder = new MarkerFinder();
        markerSearchStarted = sectionClock.Elapsed.TotalSeconds;
        ReportStatus("Finding markers. In ACC, retry from your checkpoint and drive the corner as usual.");
    }

    private static void PollMarkerFinder()
    {
        if (markerFinder == null) return;
        double now = sectionClock.Elapsed.TotalSeconds;
        if (now < nextMarkerSample) return;
        nextMarkerSample = now + 0.025;
        if (now - markerSearchStarted > MarkerSearchSeconds)
        {
            markerFinder = null;
            ReportStatus("Marker search timed out. Select Find markers to try again.");
            return;
        }
        if (reloadInProgress || !FindGame()) return;
        var finder = markerFinder;
        if (!finder.Observe(ReadTelemetry(false))) return;

        markerFinder = null;
        if (finder.Failure != null)
        {
            ReportStatus("Couldn't place the markers. " + finder.Failure);
            return;
        }
        ApplyCommand(() => ApplyMarkers(finder.Track, finder.Start.Value, finder.Finish.Value, string.Format(
            "Markers placed: start {0:0.00%}, just before you braked at {1:0.00%}; finish {2:0.00%}, once you were flat out.",
            WrapPosition(finder.Start.Value), finder.BrakePoint.Value, WrapPosition(finder.Finish.Value))));
    }

    // Engine thread: moves one marker by a tenth of a percent of the lap per step.
    private static void NudgeMarker(bool start, int steps)
    {
        var section = settings.Sections.Find(candidate => candidate.Checkpoint == settings.ActiveCheckpoint &&
            candidate.Finish.HasValue && (ActiveCheckpoint() == null || SameTrack(candidate.Track, ActiveCheckpoint().Track)));
        if (section == null)
            throw new InvalidOperationException("Set both markers before fine-tuning them.");
        double moved = (start ? section.Start : section.Finish.Value) + steps * MarkerNudgeStep;
        ApplyMarkers(section.Track, start ? moved : section.Start, start ? section.Finish.Value : moved,
            string.Format("{0} moved to {1:0.00%}.", start ? "Start" : "Finish", WrapPosition(moved)));
    }

    private sealed class MarkerFinder
    {
        public const double BrakeOn = 0.3, FullThrottle = 0.95, LeadSeconds = 0.5, ExitHoldSeconds = 1.0,
            MinimumLeadSeconds = 0.3, GiveUpDistance = 0.4;
        private readonly List<TelemetryFrame> frames = new List<TelemetryFrame>();
        private int brakeIndex = -1, slowestIndex = -1;
        private double? flatOutSince;

        public double? Start, Finish, BrakePoint;
        public string Failure;
        public string Track { get { return frames.Count == 0 ? null : frames[0].Track; } }

        // Returns true once the markers are found or the search has failed.
        public bool Observe(TelemetryFrame frame)
        {
            if (frame == null) return false;
            if (frames.Count != 0)
            {
                var last = frames[frames.Count - 1];
                if (frame.Packet == last.Packet) return false;
                double step = SectionTimer.ForwardDistance(last.Position, frame.Position);
                // A reload or a jump back to the checkpoint starts the search again.
                if (!SameTrack(frame.Track, last.Track) || (step > 0.02 && step < 0.98) || frame.Time - last.Time > 1)
                    Clear();
            }
            if (frames.Count == 0 && frame.Speed < 30) return false;
            frames.Add(frame);
            int index = frames.Count - 1;

            if (brakeIndex < 0)
            {
                if (frame.Brake >= BrakeOn && frame.Speed >= 60) FoundBraking(index);
            }
            else
            {
                if (slowestIndex < 0 || frame.Speed < frames[slowestIndex].Speed)
                {
                    slowestIndex = index;
                    flatOutSince = null;
                }
                if (frame.Gas >= FullThrottle && frame.Brake < 0.05)
                {
                    if (!flatOutSince.HasValue) flatOutSince = frame.Time;
                    if (frame.Time - flatOutSince.Value >= ExitHoldSeconds - 0.000001 && frame.Speed > frames[slowestIndex].Speed + 5)
                    {
                        Finish = frame.Position;
                        return true;
                    }
                }
                else flatOutSince = null;
            }

            if (SectionTimer.ForwardDistance(frames[0].Position, frame.Position) > GiveUpDistance &&
                SectionTimer.ForwardDistance(frames[0].Position, frame.Position) < 0.99)
            {
                Failure = brakeIndex < 0 ? "No braking was found. Save the checkpoint a few seconds before the corner." :
                    "You didn't go flat out after the corner. Drive through to the exit straight.";
                return true;
            }
            return false;
        }

        private void FoundBraking(int index)
        {
            brakeIndex = index;
            var braking = frames[index];
            BrakePoint = braking.Position;
            // Half a second before braking, but never right where the checkpoint loads, or a
            // retry would begin on top of the start line and miss it.
            double target = Math.Max(braking.Time - LeadSeconds, frames[0].Time + MinimumLeadSeconds);
            int startIndex = index;
            while (startIndex > 0 && frames[startIndex].Time > target + 0.000001) startIndex--;
            Start = frames[startIndex].Position;
        }

        private void Clear()
        {
            frames.Clear();
            brakeIndex = slowestIndex = -1;
            flatOutSince = null;
            Start = Finish = BrakePoint = null;
        }
    }
}
