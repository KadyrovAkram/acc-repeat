using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    // A stand-in for ACC and Windows: it shows one menu at a time, reacts to clicks on the
    // menu buttons the way ACC does, and keeps time on a clock that only moves when the
    // reset sleeps, so a 45-second wait takes no real time.
    internal sealed class SimulatedAcc : IGameWindow, IMenuScreen, IGameInput, IClock, IResetHost
    {
        public enum Menu { Driving, Pause, QuitConfirmation, Home, SavedGames }

        private static readonly Size ClientSize = new Size(1920, 1080);
        private MenuLayout layout;
        private double now = 100;
        private Point cursor;
        private double focusReturnsAt = -1;

        public Menu Showing = Menu.Driving;
        public Save SaveToLoad = new Save { Slot = "4", Created = "2026.11.01-18.00.00", Label = "Monza", Track = "monza", Mode = "Practice", Row = 0 };
        public Save ArmedLoop;
        public int SavedGamesClicksIgnored;
        public bool QuitConfirmationNeverAppears;
        public bool PlayButtonCovered;
        public double FocusLostAfterConfirmFor;
        public readonly HashSet<int> HeldKeys = new HashSet<int>();
        public readonly List<string> Clicks = new List<string>();
        public readonly List<string> Reports = new List<string>();
        public readonly List<string> LoopsDisabled = new List<string>();

        public double Elapsed { get { return now - 100; } }

        // IGameWindow
        public Point Origin { get { return new Point(200, 120); } }
        public MenuLayout Layout { get { return layout; } }
        public bool IsForeground { get { return focusReturnsAt < 0 || now >= focusReturnsAt; } }
        public bool IsOpen { get { return true; } }
        public bool IsAt(Point screenPoint) { return !(PlayButtonCovered && screenPoint == ScreenPoint(PlayButton(SaveToLoad.Row))); }
        public void ForgetLayout() { layout = null; }
        public void RefreshGeometry() { if (layout == null) layout = MenuLayout.Select(ClientSize); }
        public bool ClientSizeMatchesLayout() { return true; }

        public int PracticeStatus()
        {
            switch (Showing)
            {
                case Menu.Driving: return 2;
                case Menu.Pause: return 3;
                default: return 0;
            }
        }

        // IMenuScreen
        public bool Matches(MenuLayout current, Point origin, Signature[] signatures, int yOffset, double brightCoverage)
        {
            if (signatures == current.SaveRow) return Showing == Menu.SavedGames;
            return signatures == Labels(current, Showing);
        }

        private static Signature[] Labels(MenuLayout current, Menu menu)
        {
            switch (menu)
            {
                case Menu.Pause: return current.Pause;
                case Menu.QuitConfirmation: return current.Confirm;
                case Menu.Home: return current.Home;
                case Menu.SavedGames: return current.SavedTitle;
                default: return null;
            }
        }

        // IGameInput
        public bool IsDown(int key) { return HeldKeys.Contains(key); }
        public bool MoveCursor(Point point) { cursor = point; return true; }
        public bool TryGetCursor(out Point point) { point = cursor; return true; }

        public void Key(byte key, uint flags)
        {
            const uint keyUp = 2;
            if (key == 0x1B && flags == keyUp && Showing == Menu.Driving) Showing = Menu.Pause;
        }

        public void Click(Point point)
        {
            if (Showing == Menu.Pause && point == ScreenPoint(layout.QuitButton))
            {
                Clicks.Add("Quit");
                if (!QuitConfirmationNeverAppears) Showing = Menu.QuitConfirmation;
            }
            else if (Showing == Menu.QuitConfirmation && point == ScreenPoint(layout.ConfirmButton))
            {
                Clicks.Add("Confirm Quit");
                Showing = Menu.Home;
                if (FocusLostAfterConfirmFor > 0) focusReturnsAt = now + FocusLostAfterConfirmFor;
            }
            else if (Showing == Menu.Home && point == ScreenPoint(layout.SavedGamesButton))
            {
                Clicks.Add("Saved Games");
                if (SavedGamesClicksIgnored > 0) SavedGamesClicksIgnored--;
                else Showing = Menu.SavedGames;
            }
            else if (Showing == Menu.SavedGames && point == ScreenPoint(PlayButton(SaveToLoad.Row)))
            {
                Clicks.Add("Play row " + (SaveToLoad.Row + 1));
                Showing = Menu.Pause;
            }
            else if (Showing == Menu.Pause && point == ScreenPoint(layout.DriveButton))
            {
                Clicks.Add("Drive");
                Showing = Menu.Driving;
            }
            else
                Clicks.Add("Nothing at " + point);
        }

        private Point PlayButton(int row)
        {
            var current = layout ?? MenuLayout.Select(ClientSize);
            var point = current.PlayButtonPosition;
            point.Offset(0, current.RowOffset(row));
            return point;
        }

        private Point ScreenPoint(Point button)
        {
            var current = layout ?? MenuLayout.Select(ClientSize);
            return Point.Add(Origin, (Size)current.ToClient(button));
        }

        // IClock
        public double Now { get { return now; } }
        public void Sleep(int milliseconds) { now += milliseconds / 1000.0; }

        // IResetHost
        public bool IsClosing { get { return false; } }
        public void Publish(bool immediate) { }
        public Save SelectSave() { return SaveToLoad; }
        public Save LoopSave { get { return ArmedLoop; } }
        public void DisableLoop(string reason) { LoopsDisabled.Add(reason); }
        public void Report(string message) { Reports.Add(message); }

        public RetryController Controller() { return new RetryController(this, this, this, this, this); }
    }

    public class RetryControllerTests
    {
        private readonly SimulatedAcc acc = new SimulatedAcc();

        private void Reset(bool keyboard = true, Save expected = null)
        {
            var controller = acc.Controller();
            controller.Run(keyboard, expected);
            Assert.False(controller.InProgress);
            Assert.False(controller.WaitingForFocus);
        }

        private string LastReport { get { return acc.Reports.Last(); } }

        private static double ReloadSeconds(string report)
        {
            string number = report.Substring("Drive resumed after ".Length).Split(' ')[0];
            return double.Parse(number, CultureInfo.CurrentCulture);
        }

        [Fact]
        public void ResetFromDrivingQuitsLoadsTheSaveAndDrives()
        {
            Reset();
            Assert.Equal(new[] { "Quit", "Confirm Quit", "Saved Games", "Play row 1", "Drive" }, acc.Clicks);
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
            Assert.StartsWith("Drive resumed after ", LastReport);
            Assert.Empty(acc.LoopsDisabled);
        }

        [Fact]
        public void ResetFromSavedGamesSkipsQuitting()
        {
            acc.Showing = SimulatedAcc.Menu.SavedGames;
            Reset();
            Assert.Equal(new[] { "Play row 1", "Drive" }, acc.Clicks);
        }

        [Fact]
        public void SaveLowerInTheListIsPlayedOnItsOwnRow()
        {
            acc.SaveToLoad.Row = 3;
            Reset();
            Assert.Contains("Play row 4", acc.Clicks);
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
        }

        [Fact]
        public void SavedGamesClickIsRetriedWhileACCStaysOnTheMainMenu()
        {
            acc.SavedGamesClicksIgnored = 2;
            Reset();
            Assert.Equal(3, acc.Clicks.Count(click => click == "Saved Games"));
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
        }

        [Fact]
        public void SavedGamesIsClickedThreeTimesAtMostThenTheResetStops()
        {
            acc.SavedGamesClicksIgnored = int.MaxValue;
            Reset();
            Assert.Equal(3, acc.Clicks.Count(click => click == "Saved Games"));
            Assert.Equal("Stopped while opening Saved Games (attempt 3 of 3). " +
                "ACC stayed on the main menu after three Saved Games clicks.", LastReport);
            Assert.Equal(new[] { "reset stopped" }, acc.LoopsDisabled);
        }

        [Fact]
        public void MenuThatNeverAppearsStopsTheResetAndNamesTheStep()
        {
            acc.QuitConfirmationNeverAppears = true;
            Reset();
            Assert.Equal("Stopped while waiting for Quit confirmation. Could not recognize Quit confirmation.", LastReport);
            Assert.Equal(new[] { "Quit" }, acc.Clicks);
            Assert.InRange(acc.Elapsed, 3, 4);
        }

        [Fact]
        public void CoveredPlayButtonStopsBeforeLeavingThePracticeSession()
        {
            acc.PlayButtonCovered = true;
            Reset();
            Assert.Empty(acc.Clicks);
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
            Assert.Equal("Stopped while checking the save's Play button. " +
                "Another window is covering the save's Play button. Move it away and reset again.", LastReport);
        }

        [Fact]
        public void HoldingF9CancelsTheReset()
        {
            acc.HeldKeys.Add(0x78);
            Reset();
            Assert.Empty(acc.Clicks);
            Assert.Equal("Stopped while checking the practice save. Cancelled with F9.", LastReport);
        }

        [Fact]
        public void HeldShortcutKeysStopAKeyboardResetAfterTwoSeconds()
        {
            acc.HeldKeys.Add(0x11);
            Reset();
            Assert.Empty(acc.Clicks);
            Assert.Equal("Stopped while waiting for the shortcut keys to be released. " +
                "Release the shortcut keys promptly and try again.", LastReport);
        }

        [Fact]
        public void HeldKeysDoNotDelayAWheelReset()
        {
            acc.HeldKeys.Add(0x11);
            Reset(keyboard: false);
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
        }

        [Fact]
        public void AutoResetStopsWhenTheSelectedSaveChanged()
        {
            var armed = new Save { Slot = "5", Created = "2026.11.02-09.00.00", Track = "monza" };
            Reset(keyboard: false, expected: armed);
            Assert.Empty(acc.Clicks);
            Assert.Equal("Stopped while checking the practice save. " +
                "The selected save changed. Enable the loop again for the new save.", LastReport);
        }

        [Fact]
        public void LoopArmedForAnotherSaveIsTurnedOff()
        {
            acc.ArmedLoop = new Save { Slot = "5", Created = "2026.11.02-09.00.00", Track = "monza" };
            Reset();
            Assert.Equal(new[] { "the selected save changed" }, acc.LoopsDisabled);
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
        }

        [Fact]
        public void ResetWaitsWhileACCIsInTheBackgroundAndLeavesThatTimeOut()
        {
            acc.FocusLostAfterConfirmFor = 30;
            Reset();
            Assert.Equal(SimulatedAcc.Menu.Driving, acc.Showing);
            Assert.True(acc.Elapsed > 30);
            Assert.True(ReloadSeconds(LastReport) < acc.Elapsed - 29);
        }

        [Fact]
        public void ASecondResetStartsFromAFreshState()
        {
            var controller = acc.Controller();
            controller.Run(true, null);
            acc.Showing = SimulatedAcc.Menu.Driving;
            acc.Clicks.Clear();
            controller.Run(true, null);
            Assert.Equal(new[] { "Quit", "Confirm Quit", "Saved Games", "Play row 1", "Drive" }, acc.Clicks);
            Assert.Equal(2, acc.Reports.Count(report => report.StartsWith("Drive resumed after ")));
        }
    }
}
