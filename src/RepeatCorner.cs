using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

internal static partial class RepeatCorner
{
    private const string GameProcess = "AC2-Win64-Shipping";
    private const int HotkeyMessage = 0x0312;
    private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);
    private static IntPtr game;
    private static Point origin;
    private static MenuLayout layout;
    private static bool diagnostics;
    private static string stage;
    private static bool reloadInProgress;
    private static bool reloadWaitingForFocus;
    private static double reloadTimeAway;
    private static int reloadFocusVersion;
    private static DesktopSession desktopSession;

    private static double ReloadTime { get { return sectionClock.Elapsed.TotalSeconds - reloadTimeAway; } }

    private static int Main(string[] args)
    {
        EnableDpiAwareness();
        if (args.Length == 1 && args[0] == "--check-screen")
            return CheckScreen();
        if (args.Length == 1 && args[0] == "--list-saves")
        {
            PrintSaves();
            return 0;
        }
        return RunEngine(null);
    }

    private static int RunEngine(DesktopSession desktop)
    {
        bool exists;
        using (var instance = new Mutex(true, @"Local\ACCRepeatCorner", out exists))
        {
            if (!exists)
            {
                const string message = "ACC Repeat Corner is already running. Close the other instance and reopen this one.";
                if (desktop == null) Console.WriteLine(message);
                else desktop.Fail(message);
                return 1;
            }
            if (!RegisterHotKey(IntPtr.Zero, 1, 0x4006, 0x4A))
            {
                const string message = "Ctrl+Shift+J is already in use.";
                if (desktop == null)
                {
                    Console.WriteLine(message + " Press Enter to close.");
                    Console.ReadLine();
                }
                else desktop.Fail(message + " Free the shortcut and reopen Repeat Corner.");
                return 1;
            }
            bool startKey = RegisterHotKey(IntPtr.Zero, 2, 0x4006, 0x4B);
            bool finishKey = RegisterHotKey(IntPtr.Zero, 3, 0x4006, 0x4C);

            desktopSession = desktop;
            try
            {
                LoadSettings();
                LoadTrackMaps();
                PruneAllCuts();
                if (desktop == null)
                {
                    Console.Title = "ACC Repeat Corner";
                    Console.WriteLine("Ctrl+Shift+J: retry the selected Practice save while ACC is focused.");
                    Console.WriteLine("Ctrl+Shift+K / Ctrl+Shift+L: mark the section start / finish.");
                    if (!startKey || !finishKey)
                        Console.WriteLine("One or more section hotkeys are already in use.");
                    Console.WriteLine("Tap F9 to turn off loops. Hold F9 to cancel a reload in progress.");
                    Console.WriteLine("English ACC menus: 1080p, 1440p, 1440 x 900, or 2880 x 1800.");
                    Console.WriteLine("Type 'help' for console commands.");
                    Console.WriteLine("Active checkpoint: " + (settings.ActiveCheckpoint ?? "latest save"));
                    StartConsoleInput();
                }
                else
                    desktop.Initialize(startKey, finishKey);

                Message message;
                while (desktop == null ? ProcessConsoleInput() : desktop.ProcessCommands())
                {
                    if (PeekMessage(out message, IntPtr.Zero, HotkeyMessage, HotkeyMessage, 1))
                    {
                        if (message.WParam.ToUInt64() == 1)
                            TryRetry(true);
                        else
                            TryMarkSection(message.WParam.ToUInt64() == 2);
                    }
                    PollWheel();
                    PollTrackMap();
                    PollSection();
                    PollLoop();
                    if (desktop != null) desktop.Publish();
                    Thread.Sleep(15);
                }
            }
            finally
            {
                SaveTrackMaps();
                if (loopVoice != null) loopVoice.Dispose();
                UnregisterHotKey(IntPtr.Zero, 1);
                if (startKey) UnregisterHotKey(IntPtr.Zero, 2);
                if (finishKey) UnregisterHotKey(IntPtr.Zero, 3);
                desktopSession = null;
            }
        }
        return 0;
    }

    private static void ReportStatus(string message)
    {
        Console.WriteLine(message);
        if (desktopSession != null) desktopSession.Note(message);
    }

    private static void EnableDpiAwareness()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorAwareV2)) return;
        }
        catch (EntryPointNotFoundException) { }
        SetProcessDPIAware();
    }

    private static bool TryRetry(bool keyboard, Save expected = null)
    {
        if (!FindGame() || (expected != null && game != loopWindow)) return false;
        if (expected != null) Console.WriteLine("Auto-retrying...");
        CancelLoopCountdown();
        timer.Reset();
        reloadInProgress = true;
        HideOverlayNow();
        reloadTimeAway = 0;
        reloadFocusVersion = 0;
        layout = null;
        try { Retry(keyboard, expected); }
        catch (Exception error)
        {
            DisableLoop("retry stopped");
            ReportStatus("Stopped while " + stage + ". " + error.Message);
        }
        finally
        {
            reloadWaitingForFocus = false;
            reloadInProgress = false;
            overlaySuppressed = false;
        }
        wheelArmed = false;
        Message pending;
        while (PeekMessage(out pending, IntPtr.Zero, HotkeyMessage, HotkeyMessage, 1)) { }
        return true;
    }

    private static void Retry(bool keyboard, Save expected)
    {
        double started = ReloadTime;
        stage = "waiting for the shortcut keys to be released";
        while (keyboard && (IsDown(0x11) || IsDown(0x10) || IsDown(0x4A) || IsDown(0x5B) || IsDown(0x5C)))
        {
            EnsureGame();
            if (ReloadTime - started > 2)
                throw new InvalidOperationException("Release the shortcut keys promptly and try again.");
            Thread.Sleep(20);
        }
        stage = "checking the practice save";
        var checkpoint = SelectSave();
        if (expected != null && !expected.SameSave(checkpoint))
            throw new InvalidOperationException("The selected save changed. Enable the loop again for the new save.");
        if (loopSave != null && !loopSave.SameSave(checkpoint))
            DisableLoop("the selected save changed");
        Console.WriteLine("Retrying " + checkpoint.Label + "...");
        EnsureGame();
        int status = PracticeStatus();
        if (status != 2 && status != 3)
            throw new InvalidOperationException("Start in solo Free Practice, on the track.");
        if (status == 2 && !Matches(layout.Pause))
        {
            stage = "opening the pause menu";
            EnsureGame();
            if (PracticeStatus() == 2)
            {
                Key(0x1B, 0);
                Thread.Sleep(45);
                Key(0x1B, 2);
            }
        }

        WaitFor("Free Practice pause menu", layout.Pause, 3);
        stage = "selecting Quit";
        Click(layout.QuitButton, layout.Pause);
        WaitFor("Quit confirmation", layout.Confirm, 3);
        stage = "confirming Quit";
        Click(layout.ConfirmButton, layout.Confirm);
        OpenSavedGames();
        stage = "selecting Play on the checkpoint row";
        var play = layout.PlayButtonPosition;
        play.Offset(0, layout.RowOffset(checkpoint.Row));
        Click(play, layout.SavedTitle, checkpoint);
        WaitFor("restored checkpoint and Drive button", layout.Pause, 90);
        stage = "selecting Drive";
        Click(layout.DriveButton, layout.Pause);
        int driveFocusVersion = reloadFocusVersion;
        double driveStarted = ReloadTime;
        while (true)
        {
            bool paused = Matches(layout.Pause);
            if (!paused && PracticeStatus() == 2)
                break;
            if (paused && driveFocusVersion != reloadFocusVersion)
            {
                // ACC may have paused again after the original Drive click.
                Click(layout.DriveButton, layout.Pause);
                driveFocusVersion = reloadFocusVersion;
                driveStarted = ReloadTime;
            }
            if (ReloadTime - driveStarted > 3)
                throw new InvalidOperationException("The Drive menu did not close.");
            Thread.Sleep(100);
        }
        ReportStatus("Drive resumed after " + (ReloadTime - started).ToString("0.0") + " seconds of reload time.");
    }

    private static int PracticeStatus()
    {
        using (var map = MemoryMappedFile.OpenExisting(@"Local\acpmf_graphics", MemoryMappedFileRights.Read))
        using (var view = map.CreateViewAccessor(0, 12, MemoryMappedFileAccess.Read))
        {
            // The graphics page starts with the packet ID, game status, and session type.
            return view.ReadInt32(8) == 0 ? view.ReadInt32(4) : 0;
        }
    }

    private static void OpenSavedGames()
    {
        WaitFor("main menu", layout.Home, 45);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            stage = "opening Saved Games (attempt " + attempt + " of 3)";
            Click(layout.SavedGamesButton, layout.Home);
            if (WaitForMenu(layout.SavedTitle, 2)) return;

            // Retry only if ACC still shows the main menu. A late transition may already be underway.
            if (!Matches(layout.Home))
            {
                WaitFor("Saved Games after leaving the main menu", layout.SavedTitle, 6);
                return;
            }
            if (attempt < 3)
                Console.WriteLine("ACC is still on the main menu. Retrying Saved Games.");
        }
        throw new InvalidOperationException("ACC stayed on the main menu after three Saved Games clicks.");
    }

    private static void WaitFor(string name, Signature[] signatures, int seconds)
    {
        stage = "waiting for " + name;
        if (!WaitForMenu(signatures, seconds))
            throw new InvalidOperationException("Could not recognize " + name + ".");
    }

    private static bool WaitForMenu(Signature[] signatures, int seconds)
    {
        double started = ReloadTime;
        while (!Matches(signatures))
        {
            if (ReloadTime - started >= seconds)
                return false;
            Thread.Sleep(125);
        }
        return true;
    }

    private static bool Matches(Signature[] signatures, int yOffset = 0, double brightCoverage = 0.70)
    {
        EnsureGame();
        bool matched = true;
        foreach (var label in signatures)
        {
            var region = layout.CaptureRegion(label, yOffset);
            int padding = layout.SignaturePadding;
            var referenceSize = new Size(label.Width + 2 * padding, label.Height + 2 * padding);
            using (var capture = new Bitmap(region.Width, region.Height))
            using (var captureGraphics = Graphics.FromImage(capture))
            using (var bitmap = new Bitmap(referenceSize.Width, referenceSize.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                captureGraphics.CopyFromScreen(origin.X + region.X, origin.Y + region.Y,
                    0, 0, capture.Size);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(capture, new Rectangle(Point.Empty, referenceSize));
                if (!label.Matches(bitmap, brightCoverage, padding))
                {
                    if (!diagnostics) return false;
                    matched = false;
                }
            }
        }
        return matched;
    }

    private sealed class Signature
    {
        public readonly int X, Y, Width, Height;
        private readonly byte[] mask;

        public Signature(int x, int y, int width, int height, string encoded)
        {
            X = x; Y = y; Width = width; Height = height;
            mask = Convert.FromBase64String(encoded);
        }

        public bool Matches(Bitmap bitmap, double brightCoverage, int padding)
        {
            var brightPixels = new bool[bitmap.Width, bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                brightPixels[x, y] = pixel.R >= 120 && pixel.G >= 120 && pixel.B >= 120;
            }
            if (MatchesAt(brightPixels, brightCoverage, padding, padding, false))
                return true;

            // ACC can move individual labels slightly when changing the render resolution.
            // Retain the dark outline and allow only a one-pixel change in the bright strokes.
            for (int y = 1; y < 2 * padding; y++)
            for (int x = 1; x < 2 * padding; x++)
                if (MatchesAt(brightPixels, brightCoverage, x, y, true))
                    return true;
            return false;
        }

        private bool MatchesAt(bool[,] pixels, double brightCoverage, int left, int top, bool flexible)
        {
            int foreground = 0, background = 0, foregroundMatches = 0, nearbyMatches = 0, backgroundMatches = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                byte expected = mask[y * Width + x];
                if (expected == 0) continue;
                int column = left + x, row = top + y;
                bool bright = pixels[column, row];
                if (expected == 1)
                {
                    foreground++;
                    if (bright) foregroundMatches++;
                    if (flexible && (bright || pixels[column - 1, row] || pixels[column + 1, row] ||
                        pixels[column, row - 1] || pixels[column, row + 1]))
                        nearbyMatches++;
                }
                else
                {
                    background++;
                    if (!bright) backgroundMatches++;
                }
            }
            if (diagnostics && !flexible)
                Console.WriteLine("{0},{1}: {2}/{3} bright, {4}/{5} background",
                    X, Y, foregroundMatches, foreground, backgroundMatches, background);
            return backgroundMatches >= background * 0.78 &&
                (foregroundMatches >= foreground * brightCoverage ||
                 (flexible && foregroundMatches >= foreground * 0.55 && nearbyMatches >= foreground * 0.90));
        }
    }

    private static int CheckScreen()
    {
        if (!FindGame()) return 1;
        diagnostics = true;
        EnsureGame();
        Console.WriteLine("Origin " + origin + ", ACC client size " + layout.ClientSize);
        Console.WriteLine("Practice pause: " + Matches(layout.Pause));
        Console.WriteLine("Quit confirmation: " + Matches(layout.Confirm));
        Console.WriteLine("Main menu: " + Matches(layout.Home));
        bool savedGames = Matches(layout.SavedTitle);
        Console.WriteLine("Saved Games: " + savedGames);
        if (savedGames)
        {
            for (int row = 0; row < VisibleSaveRows; row++)
                Console.WriteLine("Practice Play row " + (row + 1) + ": " + MatchesSaveRow(row));
        }
        return 0;
    }

    private static bool FindGame()
    {
        game = GetForegroundWindow();
        uint pid;
        GetWindowThreadProcessId(game, out pid);
        if (pid == 0) return false;
        try
        {
            using (var process = Process.GetProcessById((int)pid))
                return process.ProcessName == GameProcess;
        }
        catch (ArgumentException) { return false; }
    }

    private static void EnsureGame()
    {
        CheckDesktopClosing();
        if (desktopSession != null) desktopSession.Publish();
        if (IsDown(0x78))
            throw new OperationCanceledException("Cancelled with F9.");
        if (GetForegroundWindow() != game)
        {
            if (!reloadInProgress)
                throw new InvalidOperationException("ACC lost focus. Switch back to ACC and retry.");
            WaitForReloadFocus();
        }
        var size = ReadClientSize(out origin);
        if (layout == null)
            layout = MenuLayout.Select(size);
        else if (layout.ClientSize != size)
            throw new InvalidOperationException("The ACC window changed size. Retry from the practice session.");
    }

    private static Size ReadClientSize(out Point clientOrigin)
    {
        Rect bounds;
        clientOrigin = Point.Empty;
        if (!GetClientRect(game, out bounds) || !ClientToScreen(game, ref clientOrigin))
            throw new InvalidOperationException("Could not read the ACC window size and position.");
        return new Size(bounds.Right, bounds.Bottom);
    }

    private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    private static void CheckDesktopClosing()
    {
        if (desktopSession != null && desktopSession.IsClosing)
            throw new OperationCanceledException("Repeat Corner is closing.");
    }

    private static void WaitForReloadFocus()
    {
        Console.WriteLine("Reload paused. Switch back to ACC to continue, or press F9 to cancel.");
        reloadWaitingForFocus = true;
        if (desktopSession != null) desktopSession.Publish(true);
        double awaySince = sectionClock.Elapsed.TotalSeconds;
        reloadFocusVersion++;
        // Fullscreen ACC may briefly report a different client size while it restores the display mode.
        double focusedSince = -1;
        while (true)
        {
            CheckDesktopClosing();
            if (IsDown(0x78)) throw new OperationCanceledException("Cancelled with F9.");
            if (!IsWindow(game)) throw new InvalidOperationException("ACC closed during the reload.");
            if (GetForegroundWindow() == game)
            {
                double now = sectionClock.Elapsed.TotalSeconds;
                if (focusedSince < 0) focusedSince = now;
                Point clientOrigin;
                bool restored = layout == null || ReadClientSize(out clientOrigin) == layout.ClientSize;
                if (now - focusedSince >= 0.25 && (restored || now - focusedSince >= 2))
                    break;
            }
            else
                focusedSince = -1;
            Thread.Sleep(25);
        }
        reloadTimeAway += sectionClock.Elapsed.TotalSeconds - awaySince;
        reloadWaitingForFocus = false;
        Console.WriteLine("ACC focused. Checking the menu before continuing.");
    }

    private static void Click(Point button, Signature[] expected, Save checkpoint = null)
    {
        double started = ReloadTime;
        while (ReloadTime - started < 4)
        {
            EnsureGame();
            int focusVersion = reloadFocusVersion;
            var offset = layout.ToClient(button);
            var point = Point.Add(origin, (Size)offset);
            if (Matches(expected) && GetForegroundWindow() == game &&
                WindowFromPoint(point) == game && !IsDown(0x01) && !IsDown(0x02) && SetCursorPos(point.X, point.Y))
            {
                // Hover reveals Play. Verify the same screen again before sending the click.
                Thread.Sleep(65);
                if (Matches(expected) && (checkpoint == null || MatchesSaveRow(checkpoint.Row)))
                {
                    EnsureGame();
                    if (checkpoint != null)
                    {
                        var selected = SelectSave();
                        if (!checkpoint.SameSave(selected) || checkpoint.Row != selected.Row)
                            throw new InvalidOperationException("The save list changed during retry.");
                    }
                    Point cursor;
                    if ((expected != layout.Pause || PracticeStatus() == 3) &&
                        focusVersion == reloadFocusVersion && point == Point.Add(origin, (Size)offset) && GetForegroundWindow() == game &&
                        WindowFromPoint(point) == game && GetCursorPos(out cursor) &&
                        cursor == point && !IsDown(0x01) && !IsDown(0x02))
                    {
                        CheckDesktopClosing();
                        SendClick(point);
                        return;
                    }
                }
            }
            Thread.Sleep(125);
        }
        throw new InvalidOperationException("Could not confirm the expected ACC menu button.");
    }

    private static bool MatchesSaveRow(int row)
    {
        // Row spacing is fractional in ACC, so scaled text varies slightly by row.
        return Matches(layout.SaveRow, layout.RowOffset(row), 0.62);
    }

    private static void SendClick(Point point)
    {
        // Batch positioning and both button events so physical mouse movement cannot split them.
        int x = AbsoluteCoordinate(point.X, GetSystemMetrics(76), GetSystemMetrics(78));
        int y = AbsoluteCoordinate(point.Y, GetSystemMetrics(77), GetSystemMetrics(79));
        const uint absoluteVirtualMove = 0xC001;
        var events = new[]
        {
            new Input { Mouse = new MouseInput { X = x, Y = y, Flags = absoluteVirtualMove | 2 } },
            new Input { Mouse = new MouseInput { X = x, Y = y, Flags = absoluteVirtualMove | 4 } }
        };
        if (SendInput((uint)events.Length, events, Marshal.SizeOf(typeof(Input))) != events.Length)
            throw new InvalidOperationException("Windows could not send the ACC menu click.");
    }

    private static int AbsoluteCoordinate(int pixel, int start, int size)
    {
        return (int)(((long)(pixel - start) * 65536 + 32768) / size);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void Key(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    private static void Key(byte key, uint flags) { Key(key, 0, flags, UIntPtr.Zero); }
}
