using System;
using System.Speech.Synthesis;

internal static partial class RepeatCorner
{
    private static Save loopSave;
    private static int loopDelay;
    private static LoopCountdown countdown;
    private static IntPtr loopWindow;
    private static double nextLoopCheck;
    private static double? telemetryMissingSince;
    private static SpeechSynthesizer loopVoice;
    private static Prompt resultAnnouncement;
    private static bool voiceUnavailable;

    private static void ConfigureLoop(string arguments)
    {
        var parts = arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine(loopSave == null ? "Hands-free loop is off." :
                string.Format("Hands-free loop is on. {0}-second countdown for {1}.", loopDelay, loopSave.Label));
            return;
        }
        if (parts.Length == 1 && parts[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (loopSave == null) Console.WriteLine("Hands-free loop is already off.");
            else DisableLoop("turned off");
            return;
        }
        int delay = 3;
        if (!parts[0].Equals("on", StringComparison.OrdinalIgnoreCase) || parts.Length > 2 ||
            (parts.Length == 2 && (!int.TryParse(parts[1], out delay) || delay < 1 || delay > 30)))
            throw new InvalidOperationException("Use 'loop on [1-30]', 'loop off', or 'loop'.");

        var save = SelectSave();
        var section = CurrentSection(save.Track);
        if (section == null || !section.Finish.HasValue)
            throw new InvalidOperationException("Mark both timing lines for the selected save before enabling the loop.");

        CancelLoopCountdown();
        loopSave = save;
        loopDelay = delay;
        ReportStatus(string.Format("Hands-free loop armed for {0}. Auto-retry starts {1} s after the result callout.",
            settings.ActiveCheckpoint ?? save.Label, delay));
    }

    private static void StartLoopCountdown(Section section, string announcement)
    {
        if (loopSave == null || countdown != null) return;
        if (!SameTrack(section.Track, loopSave.Track))
        {
            DisableLoop("the current track differs from the selected save");
            return;
        }
        loopWindow = game;
        resultAnnouncement = SpeakLoop(announcement);
        countdown = new LoopCountdown(sectionClock.Elapsed.TotalSeconds, loopDelay);
        nextLoopCheck = 0;
        telemetryMissingSince = null;
    }

    private static void PollLoop()
    {
        if (loopSave == null) return;
        if (IsDown(0x78))
        {
            DisableLoop("cancelled with F9");
            return;
        }
        if (countdown == null) return;
        double now = sectionClock.Elapsed.TotalSeconds;
        if (GetForegroundWindow() != loopWindow)
        {
            if (!IsWindow(loopWindow))
            {
                DisableLoop("ACC closed");
                return;
            }
            telemetryMissingSince = null;
            SuspendLoop(now);
            return;
        }

        if (now < nextLoopCheck) return;
        nextLoopCheck = now + 0.05;
        var frame = ReadTelemetry(true);
        if (frame == null)
        {
            // ACC can briefly drop its telemetry before Windows reports the new foreground window.
            if (!telemetryMissingSince.HasValue) telemetryMissingSince = now;
            if (now - telemetryMissingSince.Value >= 1 && GetForegroundWindow() == loopWindow)
                DisableLoop("ACC left solo Free Practice during the countdown");
            else
                SuspendLoop(now);
            return;
        }
        telemetryMissingSince = null;
        if (!SameTrack(frame.Track, loopSave.Track))
        {
            DisableLoop("the current track differs from the selected save");
            return;
        }
        if (frame.Paused)
        {
            SuspendLoop(now);
            return;
        }
        if (countdown.Resume(now))
            Console.WriteLine("Auto-retry countdown resumed.");

        int? tick = countdown.Update(now, resultAnnouncement == null || resultAnnouncement.IsCompleted);
        if (!tick.HasValue) return;
        if (resultAnnouncement != null && !resultAnnouncement.IsCompleted)
            CancelLoopSpeech();
        resultAnnouncement = null;
        if (tick.Value == 0)
        {
            if (!TryRetry(false, loopSave))
                SuspendLoop(now);
        }
        else
        {
            Console.WriteLine("Auto-retry in {0} s. Tap F9 to cancel.", tick.Value);
            if (tick.Value <= 3) SpeakLoop(tick.Value.ToString());
        }
    }

    private static void SuspendLoop(double now)
    {
        if (!countdown.Suspend(now)) return;
        resultAnnouncement = null;
        CancelLoopSpeech();
        Console.WriteLine("Auto-retry paused. Return to live driving in ACC to continue.");
    }

    private static void DisableLoop(string reason)
    {
        if (loopSave == null) return;
        loopSave = null;
        CancelLoopCountdown();
        ReportStatus("Hands-free loop off (" + reason + ").");
    }

    private static void CancelLoopCountdown()
    {
        if (countdown == null) return;
        countdown = null;
        resultAnnouncement = null;
        CancelLoopSpeech();
    }

    private static Prompt SpeakLoop(string text)
    {
        if (voiceUnavailable) return null;
        try
        {
            if (loopVoice == null) loopVoice = new SpeechSynthesizer();
            return loopVoice.SpeakAsync(text);
        }
        catch (Exception error)
        {
            voiceUnavailable = true;
            ReportStatus("Voice unavailable. The countdown will continue. " + error.Message);
            return null;
        }
    }

    private static void CancelLoopSpeech()
    {
        if (loopVoice == null || voiceUnavailable) return;
        try { loopVoice.SpeakAsyncCancelAll(); }
        catch (InvalidOperationException) { }
    }

    private sealed class LoopCountdown
    {
        private double voiceDeadline;
        private readonly int delay;
        private double? retryAt;
        private double? suspendedAt;
        private int lastTick = -1;

        public bool IsSuspended { get { return suspendedAt.HasValue; } }
        public int RemainingSeconds { get { return lastTick; } }

        public LoopCountdown(double now, int delay)
        {
            voiceDeadline = now + 6;
            this.delay = delay;
        }

        public int? Update(double now, bool voiceFinished)
        {
            if (IsSuspended) return null;
            if (!retryAt.HasValue)
            {
                if (!voiceFinished && now < voiceDeadline) return null;
                retryAt = now + delay;
            }
            int tick = Math.Max(0, (int)Math.Ceiling(retryAt.Value - now));
            if (tick == lastTick) return null;
            lastTick = tick;
            return tick;
        }

        public bool Suspend(double now)
        {
            if (IsSuspended) return false;
            suspendedAt = now;
            return true;
        }

        public bool Resume(double now)
        {
            if (!IsSuspended) return false;
            double timeAway = now - suspendedAt.Value;
            voiceDeadline += timeAway;
            if (retryAt.HasValue) retryAt += timeAway;
            suspendedAt = null;
            lastTick = -1;
            return true;
        }
    }
}
