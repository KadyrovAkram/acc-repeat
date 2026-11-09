using System;
using System.Drawing;

internal static partial class RepeatCorner
{
    // What a reset needs from the rest of the engine.
    internal interface IResetHost
    {
        // The app window is closing, so a reset in progress stops.
        bool IsClosing { get; }
        // Publishes a fresh snapshot to the window so it shows the current step.
        void Publish(bool immediate);
        // The save the reset loads, read again from ACC's save files.
        Save SelectSave();
        // The save the hands-free loop is armed for, or null.
        Save LoopSave { get; }
        void DisableLoop(string reason);
        void Report(string message);
    }

    // Resets ACC to a save by driving its menus: quit the session, open Saved Games, select
    // Play on the save's row, then select Drive. Every step waits until the expected menu is
    // recognized before it clicks, and a failure stops the reset and names the step.
    internal sealed class RetryController
    {
        private const string PlayCovered = "Another window is covering the save's Play button. Move it away and reset again.";

        private readonly IGameWindow game;
        private readonly IMenuScreen screen;
        private readonly IGameInput input;
        private readonly IClock clock;
        private readonly IResetHost host;
        private string stage;
        private bool inProgress;
        private bool waitingForFocus;
        private double timeAway;
        private int focusVersion;

        public RetryController(IGameWindow game, IMenuScreen screen, IGameInput input, IClock clock, IResetHost host)
        {
            this.game = game;
            this.screen = screen;
            this.input = input;
            this.clock = clock;
            this.host = host;
        }

        public bool InProgress { get { return inProgress; } }
        public bool WaitingForFocus { get { return waitingForFocus; } }
        // The step the reset is on, or the step it stopped at.
        public string Stage { get { return stage; } }

        // Time spent on the reset, leaving out time the player spent away from ACC.
        private double ReloadTime { get { return clock.Now - timeAway; } }

        // Runs one reset. Failures are logged and reported rather than thrown.
        public void Run(bool keyboard, Save expected)
        {
            inProgress = true;
            timeAway = 0;
            focusVersion = 0;
            game.ForgetLayout();
            try { Retry(keyboard, expected); }
            catch (Exception error)
            {
                host.DisableLoop("reset stopped");
                DiagnosticLog.Error("Reset stopped while " + stage + ".", error);
                host.Report("Stopped while " + stage + ". " + error.Message);
            }
            finally
            {
                waitingForFocus = false;
                inProgress = false;
            }
        }

        // Every reset step goes through here, so the log shows exactly how far a failed reset got.
        private void SetStage(string name)
        {
            stage = name;
            DiagnosticLog.Info("Reset step: " + name + ".");
        }

        private void Retry(bool keyboard, Save expected)
        {
            double started = ReloadTime;
            SetStage("waiting for the shortcut keys to be released");
            while (keyboard && (input.IsDown(0x11) || input.IsDown(0x10) || input.IsDown(0x4A) ||
                input.IsDown(0x5B) || input.IsDown(0x5C)))
            {
                EnsureGame();
                if (ReloadTime - started > 2)
                    throw new InvalidOperationException("Release the shortcut keys promptly and try again.");
                clock.Sleep(20);
            }
            SetStage("checking the practice save");
            var checkpoint = host.SelectSave();
            if (expected != null && !expected.SameSave(checkpoint))
                throw new InvalidOperationException("The selected save changed. Enable the loop again for the new save.");
            if (host.LoopSave != null && !host.LoopSave.SameSave(checkpoint))
                host.DisableLoop("the selected save changed");
            Console.WriteLine("Resetting to " + checkpoint.Label + "...");
            DiagnosticLog.Info(string.Format("Resetting to {0} (track {1}, Saved Games row {2}).",
                checkpoint.Label, checkpoint.Track, checkpoint.Row + 1));
            EnsureGame();
            if (!Matches(game.Layout.SavedTitle)) ExitPracticeForRetry(checkpoint);
            SetStage("selecting Play on the save's row");
            Click(CheckpointPlayButton(checkpoint), game.Layout.SavedTitle, checkpoint);
            WaitFor("restored save and Drive button", game.Layout.Pause, 90);
            SetStage("selecting Drive");
            Click(game.Layout.DriveButton, game.Layout.Pause);
            int driveFocusVersion = focusVersion;
            double driveStarted = ReloadTime;
            while (true)
            {
                bool paused = Matches(game.Layout.Pause);
                if (!paused && game.PracticeStatus() == 2)
                    break;
                if (paused && driveFocusVersion != focusVersion)
                {
                    // ACC may have paused again after the original Drive click.
                    Click(game.Layout.DriveButton, game.Layout.Pause);
                    driveFocusVersion = focusVersion;
                    driveStarted = ReloadTime;
                }
                if (ReloadTime - driveStarted > 3)
                    throw new InvalidOperationException("The Drive menu did not close.");
                clock.Sleep(100);
            }
            host.Report("Drive resumed after " + (ReloadTime - started).ToString("0.0") + " seconds of reload time.");
        }

        private Point CheckpointPlayButton(Save checkpoint)
        {
            var point = game.Layout.PlayButtonPosition;
            point.Offset(0, game.Layout.RowOffset(checkpoint.Row));
            return point;
        }

        private void ExitPracticeForRetry(Save checkpoint)
        {
            int status = game.PracticeStatus();
            if (status != 2 && status != 3)
                throw new InvalidOperationException("Start in solo Free Practice or on the Saved Games screen.");

            SetStage("checking the save's Play button");
            EnsureGame();
            var play = Point.Add(game.Origin, (Size)game.Layout.ToClient(CheckpointPlayButton(checkpoint)));
            if (game.IsForeground && !game.IsAt(play))
                throw new InvalidOperationException(PlayCovered);

            if (status == 2 && !Matches(game.Layout.Pause))
            {
                SetStage("opening the pause menu");
                EnsureGame();
                if (game.PracticeStatus() == 2)
                {
                    input.Key(0x1B, 0);
                    clock.Sleep(45);
                    input.Key(0x1B, 2);
                }
            }

            WaitFor("Free Practice pause menu", game.Layout.Pause, 3);
            SetStage("selecting Quit");
            Click(game.Layout.QuitButton, game.Layout.Pause);
            WaitFor("Quit confirmation", game.Layout.Confirm, 3);
            SetStage("confirming Quit");
            Click(game.Layout.ConfirmButton, game.Layout.Confirm);
            OpenSavedGames();
        }

        private void OpenSavedGames()
        {
            WaitFor("main menu", game.Layout.Home, 45);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                SetStage("opening Saved Games (attempt " + attempt + " of 3)");
                Click(game.Layout.SavedGamesButton, game.Layout.Home);
                if (WaitForMenu(game.Layout.SavedTitle, 2)) return;

                // Retry only if ACC still shows the main menu. A late transition may already be underway.
                if (!Matches(game.Layout.Home))
                {
                    WaitFor("Saved Games after leaving the main menu", game.Layout.SavedTitle, 6);
                    return;
                }
                if (attempt < 3)
                {
                    Console.WriteLine("ACC is still on the main menu. Retrying Saved Games.");
                    DiagnosticLog.Warn("ACC is still on the main menu. Retrying Saved Games.");
                }
            }
            throw new InvalidOperationException("ACC stayed on the main menu after three Saved Games clicks.");
        }

        private void WaitFor(string name, Signature[] signatures, int seconds)
        {
            SetStage("waiting for " + name);
            double started = ReloadTime;
            if (!WaitForMenu(signatures, seconds))
                throw new InvalidOperationException("Could not recognize " + name + ".");
            DiagnosticLog.Info(string.Format("Recognized {0} after {1:0.00} s.", name, ReloadTime - started));
        }

        private bool WaitForMenu(Signature[] signatures, int seconds)
        {
            double started = ReloadTime;
            while (!Matches(signatures))
            {
                if (ReloadTime - started >= seconds)
                    return false;
                clock.Sleep(125);
            }
            return true;
        }

        internal bool Matches(Signature[] signatures, int yOffset = 0, double brightCoverage = 0.70)
        {
            EnsureGame();
            return screen.Matches(game.Layout, game.Origin, signatures, yOffset, brightCoverage);
        }

        internal bool MatchesSaveRow(int row)
        {
            // Row spacing is fractional in ACC, so scaled text varies slightly by row.
            return Matches(game.Layout.SaveRow, game.Layout.RowOffset(row), 0.62);
        }

        // Runs before every screen read and click: stops on F9 or when the app closes, waits
        // while ACC is in the background during a reset, and reads the window's geometry.
        internal void EnsureGame()
        {
            CheckClosing();
            host.Publish(false);
            if (input.IsDown(0x78))
                throw new OperationCanceledException("Cancelled with F9.");
            if (!game.IsForeground)
            {
                if (!inProgress)
                    throw new InvalidOperationException("ACC lost focus. Switch back to ACC and reset again.");
                WaitForReloadFocus();
            }
            game.RefreshGeometry();
        }

        private void CheckClosing()
        {
            if (host.IsClosing)
                throw new OperationCanceledException("ACC Repeat is closing.");
        }

        private void WaitForReloadFocus()
        {
            Console.WriteLine("Reload paused. Switch back to ACC to continue, or press F9 to cancel.");
            waitingForFocus = true;
            host.Publish(true);
            double awaySince = clock.Now;
            focusVersion++;
            // Fullscreen ACC may briefly report a different client size while it restores the display mode.
            double focusedSince = -1;
            while (true)
            {
                CheckClosing();
                if (input.IsDown(0x78)) throw new OperationCanceledException("Cancelled with F9.");
                if (!game.IsOpen) throw new InvalidOperationException("ACC closed during the reload.");
                if (game.IsForeground)
                {
                    double now = clock.Now;
                    if (focusedSince < 0) focusedSince = now;
                    bool restored = game.ClientSizeMatchesLayout();
                    if (now - focusedSince >= 0.25 && (restored || now - focusedSince >= 2))
                        break;
                }
                else
                    focusedSince = -1;
                clock.Sleep(25);
            }
            timeAway += clock.Now - awaySince;
            waitingForFocus = false;
            Console.WriteLine("ACC focused. Checking the menu before continuing.");
        }

        private void Click(Point button, Signature[] expected, Save checkpoint = null)
        {
            double started = ReloadTime;
            bool playCovered = false;
            while (ReloadTime - started < 4)
            {
                EnsureGame();
                int version = focusVersion;
                var offset = game.Layout.ToClient(button);
                var point = Point.Add(game.Origin, (Size)offset);
                bool expectedMenu = Matches(expected) && game.IsForeground;
                bool gameAtButton = expectedMenu && game.IsAt(point);
                playCovered = checkpoint != null && expectedMenu && !gameAtButton;
                if (gameAtButton && !input.IsDown(0x01) && !input.IsDown(0x02) && input.MoveCursor(point))
                {
                    // Hover reveals Play. Verify the same screen again before sending the click.
                    clock.Sleep(65);
                    if (Matches(expected) && (checkpoint == null || MatchesSaveRow(checkpoint.Row)))
                    {
                        EnsureGame();
                        if (checkpoint != null)
                        {
                            var selected = host.SelectSave();
                            if (!checkpoint.SameSave(selected) || checkpoint.Row != selected.Row)
                                throw new InvalidOperationException("The save list changed during the reset.");
                        }
                        Point cursor;
                        if ((expected != game.Layout.Pause || game.PracticeStatus() == 3) &&
                            version == focusVersion && point == Point.Add(game.Origin, (Size)offset) && game.IsForeground &&
                            game.IsAt(point) && input.TryGetCursor(out cursor) &&
                            cursor == point && !input.IsDown(0x01) && !input.IsDown(0x02))
                        {
                            CheckClosing();
                            input.Click(point);
                            return;
                        }
                    }
                }
                clock.Sleep(125);
            }
            throw new InvalidOperationException(playCovered ? PlayCovered : "Could not confirm the expected ACC menu button.");
        }
    }
}
