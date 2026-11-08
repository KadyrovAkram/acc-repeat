using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

internal static partial class RepeatCorner
{
    // The engine thread owns settings and game input. The window reads completed snapshots.
    private sealed class DesktopSession
    {
        private readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        private Thread worker;
        private volatile bool closing;
        private volatile DeskSnapshot snapshot;
        private string message = "";
        private string shortcutWarning = "";
        private Save[] saves = new Save[0];
        private Save loadPositionSave;
        private double? loadPosition;
        private string savesError;
        private bool connected;
        private double nextRefresh, nextPublish;

        public bool IsClosing { get { return closing; } }
        public DeskSnapshot Snapshot { get { return snapshot; } }

        public void Start()
        {
            worker = new Thread(() =>
            {
                try { RunEngine(this); }
                catch (Exception error) { Fail(error.Message); }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        public void Stop() { closing = true; }
        public void Join() { if (worker != null) worker.Join(); }
        public void Post(Action action) { if (!closing) commands.Enqueue(action); }
        public void Note(string text) { message = text; }

        public void Initialize(bool startKey, bool finishKey)
        {
            if (!startKey || !finishKey)
                shortcutWarning = "A timing shortcut is in use. Free Ctrl+Shift+K and Ctrl+Shift+L, then reopen the app.";
            Publish(true);
        }

        public void Fail(string error)
        {
            snapshot = new DeskSnapshot { Error = error };
        }

        public bool ProcessCommands()
        {
            Action action;
            if (closing) return false;
            while (commands.TryDequeue(out action))
            {
                if (closing) return false;
                ApplyCommand(action);
                nextRefresh = 0;
                Publish(true);
            }
            return !closing;
        }

        public void Publish(bool immediate = false)
        {
            double now = sectionClock.Elapsed.TotalSeconds;
            if (!immediate && now < nextPublish) return;
            nextPublish = now + 0.15;

            bool refreshed = !reloadInProgress && now >= nextRefresh;
            if (refreshed)
            {
                nextRefresh = now + 1;
                RefreshSaveList();
            }

            Save selected = null;
            string checkpointDetail = savesError;
            if (savesError == null)
            {
                try { selected = SelectSave(saves, ActiveCheckpoint()); }
                catch (InvalidOperationException error) { checkpointDetail = error.Message; }
            }
            if (selected != null)
                checkpointDetail = (selected.TrackDisplayName ?? selected.Track) + " · Free Practice";

            if (selected != null && (loadPositionSave == null || !selected.SameSave(loadPositionSave) ||
                (refreshed && !loadPosition.HasValue)))
            {
                loadPosition = ReadLoadPosition(Path.Combine(SaveDirectory, selected.Slot + "R.json"));
                loadPositionSave = selected;
            }

            var activePin = FindCheckpoint(settings.ActiveCheckpoint);
            string track = selected != null ? selected.Track : activePin == null ? null : activePin.Track;
            var section = track == null ? null : CurrentSection(track);
            snapshot = new DeskSnapshot
            {
                Connected = connected,
                InPracticeSession = connected && !reloadInProgress && settings.Overlay != "Off" && IsPracticeSessionActive(),
                PinNames = settings.Checkpoints.Select(pin => pin.Name).ToArray(),
                ActivePin = settings.ActiveCheckpoint,
                AvailableSaves = saves.Where(save => save.Row < VisibleSaveRows && save.Mode == "Practice").ToArray(),
                CheckpointDetail = checkpointDetail,
                Start = section == null ? (double?)null : section.Start,
                Finish = section == null ? null : section.Finish,
                LoadPosition = selected == null ? null : loadPosition,
                Recent = section == null ? new Attempt[0] : section.RecentAttempts.ToArray(),
                Best = section == null ? null : section.BestAttempt,
                Wheel = settings.Wheel,
                LearningWheel = learnWheel != null,
                LoopArmed = loopSave != null,
                Track = track,
                TrackName = selected != null && selected.TrackDisplayName != null ? selected.TrackDisplayName : track,
                Map = MapView(track),
                CanArm = selected != null && section != null && section.Finish.HasValue,
                Reloading = reloadInProgress,
                OverlayPosition = OverlayPositions.Contains(settings.Overlay) ? settings.Overlay : DefaultOverlayPosition,
                LiveStart = timer.StartedAt,
                LastCut = section != null && section == cutSection ? cutRun : null,
                LastCutShortBy = cutShortBy,
                Countdown = countdown == null || countdown.IsSuspended || countdown.RemainingSeconds < 0 ?
                    (int?)null : countdown.RemainingSeconds,
                Status = DescribeStatus(connected, selected),
                Detail = reloadInProgress ? "Hold F9 to cancel the reload." : message,
                ShortcutWarning = shortcutWarning
            };
        }

        private void RefreshSaveList()
        {
            var games = Process.GetProcessesByName(GameProcess);
            connected = games.Length != 0;
            foreach (var runningGame in games) runningGame.Dispose();
            try
            {
                saves = ReadSaves();
                savesError = null;
            }
            catch (Exception error)
            {
                saves = new Save[0];
                savesError = "Could not read ACC saves. " + error.Message;
            }
        }

        private static string DescribeStatus(bool connected, Save selected)
        {
            if (reloadWaitingForFocus) return "Switch back to ACC to continue";
            if (reloadInProgress) return "Retrying · " + stage;
            if (countdown != null)
            {
                if (countdown.IsSuspended) return "Loop paused · return to driving";
                return countdown.RemainingSeconds < 0 ? "Announcing result" :
                    "Auto-retry in " + countdown.RemainingSeconds + " s";
            }
            if (loopSave != null) return "Loop armed";
            if (!connected) return "Start ACC to use the loop";
            return selected == null ? "Choose a practice save" : "Ready · loop off";
        }
    }

    private sealed class DeskSnapshot
    {
        public string ActivePin, CheckpointDetail, Status, Detail, ShortcutWarning, Error, OverlayPosition, Track, TrackName;
        public TrackMapView Map;
        public double? LiveStart;
        public int? Countdown;
        public Attempt LastCut;
        public double LastCutShortBy;
        public string[] PinNames;
        public Save[] AvailableSaves;
        public Attempt[] Recent;
        public Attempt Best;
        public WheelBinding Wheel;
        public double? Start, Finish, LoadPosition;
        public bool Connected, InPracticeSession, LearningWheel, LoopArmed, CanArm, Reloading;
    }
}
