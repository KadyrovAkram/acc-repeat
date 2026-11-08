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
    private static IntPtr game;
    private static Point origin;
    private static int scale;
    private static bool diagnostics;
    private static string stage;
    private static bool reloadInProgress;
    private static double reloadTimeAway;
    private static int reloadFocusVersion;

    private static double ReloadTime { get { return sectionClock.Elapsed.TotalSeconds - reloadTimeAway; } }

    private static int Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length == 1 && args[0] == "--check-screen")
            return CheckScreen();
        if (args.Length == 1 && args[0] == "--list-saves")
        {
            PrintSaves();
            return 0;
        }

        bool exists;
        using (var instance = new Mutex(true, @"Local\ACCRepeatCorner", out exists))
        {
            if (!exists)
            {
                Console.WriteLine("ACC Repeat Corner is already running.");
                return 1;
            }
            if (!RegisterHotKey(IntPtr.Zero, 1, 0x4006, 0x4A))
            {
                Console.WriteLine("Ctrl+Shift+J is already in use. Press Enter to close.");
                Console.ReadLine();
                return 1;
            }
            bool startKey = RegisterHotKey(IntPtr.Zero, 2, 0x4006, 0x4B);
            bool finishKey = RegisterHotKey(IntPtr.Zero, 3, 0x4006, 0x4C);

            try
            {
                Console.Title = "ACC Repeat Corner";
                LoadSettings();
                Console.WriteLine("Ctrl+Shift+J: retry the selected Practice save while ACC is focused.");
                Console.WriteLine("Ctrl+Shift+K / Ctrl+Shift+L: mark the section start / finish.");
                if (!startKey || !finishKey)
                    Console.WriteLine("One or more section hotkeys are already in use.");
                Console.WriteLine("Tap F9 to turn off loops. Hold F9 to cancel a reload in progress.");
                Console.WriteLine("Prototype layout: 2880 x 1800 or 1440 x 900, English menus.");
                Console.WriteLine("Type 'help' for console commands.");
                Console.WriteLine("Active checkpoint: " + (settings.ActiveCheckpoint ?? "latest save"));
                StartConsoleInput();
                Message message;
                while (ProcessConsoleInput())
                {
                    if (PeekMessage(out message, IntPtr.Zero, HotkeyMessage, HotkeyMessage, 1))
                    {
                        if (message.WParam.ToUInt64() == 1)
                            TryRetry(true);
                        else
                            TryMarkSection(message.WParam.ToUInt64() == 2);
                    }
                    PollWheel();
                    PollSection();
                    PollLoop();
                    Thread.Sleep(15);
                }
            }
            finally
            {
                if (loopVoice != null) loopVoice.Dispose();
                UnregisterHotKey(IntPtr.Zero, 1);
                if (startKey) UnregisterHotKey(IntPtr.Zero, 2);
                if (finishKey) UnregisterHotKey(IntPtr.Zero, 3);
            }
        }
        return 0;
    }

    private static bool TryRetry(bool keyboard, Save expected = null)
    {
        if (!FindGame() || (expected != null && game != loopWindow)) return false;
        if (expected != null) Console.WriteLine("Auto-retrying...");
        CancelLoopCountdown();
        timer.Reset();
        reloadInProgress = true;
        reloadTimeAway = 0;
        reloadFocusVersion = 0;
        try { Retry(keyboard, expected); }
        catch (Exception error)
        {
            Console.WriteLine("Stopped while " + stage + ": " + error.Message);
            DisableLoop("retry stopped");
        }
        finally { reloadInProgress = false; }
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
        if (status == 2 && !Matches(PracticeMenu))
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

        WaitFor("Free Practice pause menu", PracticeMenu, 3);
        stage = "selecting Quit";
        Click(719, 641, PracticeMenu);
        WaitFor("Quit confirmation", ConfirmQuit, 3);
        stage = "confirming Quit";
        Click(597, 514, ConfirmQuit);
        WaitFor("main menu", HomeMenu, 45, HomeMenuHovered);
        stage = "opening Saved Games";
        Click(522, 638, HomeMenu, HomeMenuHovered);
        WaitFor("Saved Games", SavedGamesTitle, 6);
        stage = "selecting Play on the checkpoint row";
        Click(1099, 237 + checkpoint.Row * 61, SavedGamesTitle, checkpoint: checkpoint);
        WaitFor("restored checkpoint and Drive button", PracticeMenu, 90);
        stage = "selecting Drive";
        Click(719, 366, PracticeMenu);
        int driveFocusVersion = reloadFocusVersion;
        double driveStarted = ReloadTime;
        while (true)
        {
            bool paused = Matches(PracticeMenu);
            if (!paused && PracticeStatus() == 2)
                break;
            if (paused && driveFocusVersion != reloadFocusVersion)
            {
                // ACC may have paused again after the original Drive click.
                Click(719, 366, PracticeMenu);
                driveFocusVersion = reloadFocusVersion;
                driveStarted = ReloadTime;
            }
            if (ReloadTime - driveStarted > 3)
                throw new InvalidOperationException("The Drive menu did not close.");
            Thread.Sleep(100);
        }
        Console.WriteLine("Drive resumed after " + (ReloadTime - started).ToString("0.0") + " seconds of reload time.");
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

    private static void WaitFor(string name, Signature[] signatures, int seconds, Signature[] alternative = null)
    {
        stage = "waiting for " + name;
        double started = ReloadTime;
        while (!MatchesEither(signatures, alternative))
        {
            if (ReloadTime - started >= seconds)
                throw new InvalidOperationException("Could not recognize " + name + ".");
            Thread.Sleep(125);
        }
    }

    private static bool MatchesEither(Signature[] expected, Signature[] alternative)
    {
        return Matches(expected) || (alternative != null && Matches(alternative));
    }

    private static bool Matches(Signature[] signatures, int yOffset = 0, double brightCoverage = 0.70)
    {
        EnsureGame();
        bool matched = true;
        foreach (var label in signatures)
        {
            using (var capture = new Bitmap(label.Width * scale, label.Height * scale))
            using (var captureGraphics = Graphics.FromImage(capture))
            using (var bitmap = new Bitmap(label.Width, label.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                captureGraphics.CopyFromScreen(origin.X + label.X * scale, origin.Y + (label.Y + yOffset) * scale,
                    0, 0, capture.Size);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(capture, new Rectangle(0, 0, label.Width, label.Height));
                if (!label.Matches(bitmap, brightCoverage))
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

        public bool Matches(Bitmap bitmap, double brightCoverage)
        {
            int foreground = 0, background = 0, foregroundMatches = 0, backgroundMatches = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                byte expected = mask[y * Width + x];
                if (expected == 0) continue;
                var pixel = bitmap.GetPixel(x, y);
                bool bright = pixel.R >= 120 && pixel.G >= 120 && pixel.B >= 120;
                if (expected == 1)
                {
                    foreground++;
                    if (bright) foregroundMatches++;
                }
                else
                {
                    background++;
                    if (!bright) backgroundMatches++;
                }
            }
            if (diagnostics)
                Console.WriteLine("{0},{1}: {2}/{3} bright, {4}/{5} background",
                    X, Y, foregroundMatches, foreground, backgroundMatches, background);
            return foregroundMatches >= foreground * brightCoverage && backgroundMatches >= background * 0.78;
        }
    }

    private static int CheckScreen()
    {
        if (!FindGame()) return 1;
        diagnostics = true;
        EnsureGame();
        Console.WriteLine("Origin " + origin + ", scale " + scale);
        Console.WriteLine("Practice pause: " + Matches(PracticeMenu));
        Console.WriteLine("Quit confirmation: " + Matches(ConfirmQuit));
        Console.WriteLine("Main menu: " + Matches(HomeMenu));
        Console.WriteLine("Saved Games: " + Matches(SavedGames));
        if (Matches(SavedGamesTitle))
        {
            for (int row = 0; row < VisibleSaveRows; row++)
                Console.WriteLine("Practice Play row " + (row + 1) + ": " + MatchesSaveRow(row * 61));
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
        if (IsDown(0x78))
            throw new OperationCanceledException("Cancelled with F9.");
        if (GetForegroundWindow() != game)
        {
            if (!reloadInProgress)
                throw new InvalidOperationException("ACC lost focus. Switch back to ACC and retry.");
            WaitForReloadFocus();
        }
        Rect bounds;
        origin = Point.Empty;
        if (!GetClientRect(game, out bounds) || !ClientToScreen(game, ref origin) ||
            (bounds.Right != 1440 && bounds.Right != 2880) || bounds.Bottom != bounds.Right / 8 * 5)
            throw new InvalidOperationException("This prototype requires a 2880 x 1800 or 1440 x 900 ACC window. Detected " +
                bounds.Right + " x " + bounds.Bottom + ".");
        scale = bounds.Right / 1440;
    }

    private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    private static void WaitForReloadFocus()
    {
        Console.WriteLine("Reload paused. Switch back to ACC to continue, or press F9 to cancel.");
        double awaySince = sectionClock.Elapsed.TotalSeconds;
        reloadFocusVersion++;
        while (GetForegroundWindow() != game)
        {
            if (IsDown(0x78)) throw new OperationCanceledException("Cancelled with F9.");
            if (!IsWindow(game)) throw new InvalidOperationException("ACC closed during the reload.");
            Thread.Sleep(25);
        }
        reloadTimeAway += sectionClock.Elapsed.TotalSeconds - awaySince;
        Console.WriteLine("ACC focused. Checking the menu before continuing.");
    }

    private static void Click(int x, int y, Signature[] expected, Signature[] alternative = null, Save checkpoint = null)
    {
        double started = ReloadTime;
        while (ReloadTime - started < 4)
        {
            EnsureGame();
            int focusVersion = reloadFocusVersion;
            var point = new Point(origin.X + x * scale, origin.Y + y * scale);
            if (MatchesEither(expected, alternative) && GetForegroundWindow() == game &&
                WindowFromPoint(point) == game && !IsDown(0x01) && !IsDown(0x02) && SetCursorPos(point.X, point.Y))
            {
                // Hover reveals Play. Verify the same screen again before sending the click.
                Thread.Sleep(65);
                if (MatchesEither(expected, alternative) && (checkpoint == null || MatchesSaveRow(checkpoint.Row * 61)))
                {
                    EnsureGame();
                    if (checkpoint != null)
                    {
                        var selected = SelectSave();
                        if (!checkpoint.SameSave(selected) || checkpoint.Row != selected.Row)
                            throw new InvalidOperationException("The save list changed during retry.");
                    }
                    Point cursor;
                    if (focusVersion == reloadFocusVersion && GetForegroundWindow() == game &&
                        WindowFromPoint(point) == game && GetCursorPos(out cursor) &&
                        cursor == point && !IsDown(0x01) && !IsDown(0x02))
                    {
                        SendClick(point);
                        return;
                    }
                }
            }
            Thread.Sleep(125);
        }
        throw new InvalidOperationException("Could not confirm the expected ACC menu button.");
    }

    private static bool MatchesSaveRow(int yOffset)
    {
        // Row spacing is fractional in ACC, so scaled text varies slightly by row.
        return Matches(PracticePlayRow, yOffset, 0.62);
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
