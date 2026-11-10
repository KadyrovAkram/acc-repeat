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
    private static DesktopSession desktopSession;

    // The engine's parts. The engine thread is the only thread that uses them; the window
    // reads DeskSnapshot copies instead. Tests build their own instances.
    private static readonly EngineClock clock = new EngineClock();
    private static readonly SettingsStore store = new SettingsStore(SettingsStore.DefaultPath);
    private static readonly GameConnection game = new GameConnection(clock);
    private static readonly ScreenCapture screen = new ScreenCapture();
    private static readonly RetryController resets =
        new RetryController(game, screen, new DesktopInput(), clock, new EngineResetHost());

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
        using (var instance = new Mutex(true, @"Local\ACCRepeat", out exists))
        {
            if (!exists)
            {
                const string message = "ACC Repeat is already running. Close the other instance and reopen this one.";
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
                else desktop.Fail(message + " Free the shortcut and reopen ACC Repeat.");
                return 1;
            }
            bool startKey = RegisterHotKey(IntPtr.Zero, 2, 0x4006, 0x4B);
            bool finishKey = RegisterHotKey(IntPtr.Zero, 3, 0x4006, 0x4C);

            desktopSession = desktop;
            DiagnosticLog.Started();
            if (!startKey || !finishKey)
                DiagnosticLog.Warn("One or more section hotkeys (Ctrl+Shift+K / L) are already in use.");
            try
            {
                store.Load();
                LoadTrackMaps();
                if (desktop == null)
                {
                    Console.Title = "ACC Repeat";
                    Console.WriteLine("Ctrl+Shift+J: reset to the selected Practice save while ACC is focused.");
                    Console.WriteLine("Ctrl+Shift+K / Ctrl+Shift+L: mark the section start / finish.");
                    if (!startKey || !finishKey)
                        Console.WriteLine("One or more section hotkeys are already in use.");
                    Console.WriteLine("Tap F9 to turn off loops. Hold F9 to cancel a reload in progress.");
                    Console.WriteLine("English ACC menus: 1080p, 1440p, 1440 x 900, or 2880 x 1800.");
                    Console.WriteLine("Type 'help' for console commands.");
                    Console.WriteLine("Active save: " + (store.Settings.ActiveCheckpoint ?? "latest save"));
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
        DiagnosticLog.Info(message);
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
        if (!game.Find() || (expected != null && game.Handle != loopWindow)) return false;
        if (expected != null) Console.WriteLine("Auto-resetting...");
        DiagnosticLog.Info(expected != null ? "Auto-reset started by the loop." :
            keyboard ? "Reset started from the keyboard shortcut." : "Reset started from the wheel button.");
        CancelLoopCountdown();
        timer.Reset();
        HideOverlayNow();
        try { resets.Run(keyboard, expected); }
        finally { overlaySuppressed = false; }
        wheelArmed = false;
        Message pending;
        while (PeekMessage(out pending, IntPtr.Zero, HotkeyMessage, HotkeyMessage, 1)) { }
        return true;
    }

    internal sealed class Signature
    {
        public readonly int X, Y, Width, Height;
        private readonly byte[] mask;

        public Signature(int x, int y, int width, int height, string encoded)
        {
            X = x; Y = y; Width = width; Height = height;
            mask = Convert.FromBase64String(encoded);
        }

        // With diagnostics on, the score of the exact-position check is printed to the console.
        public bool Matches(Bitmap bitmap, double brightCoverage, int padding, bool diagnostics = false)
        {
            var brightPixels = new bool[bitmap.Width, bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                brightPixels[x, y] = pixel.R >= 120 && pixel.G >= 120 && pixel.B >= 120;
            }
            if (MatchesAt(brightPixels, brightCoverage, padding, padding, false, diagnostics))
                return true;

            // ACC can move individual labels slightly when changing the render resolution.
            // Retain the dark outline and allow only a one-pixel change in the bright strokes.
            for (int y = 1; y < 2 * padding; y++)
            for (int x = 1; x < 2 * padding; x++)
                if (MatchesAt(brightPixels, brightCoverage, x, y, true, false))
                    return true;
            return false;
        }

        private bool MatchesAt(bool[,] pixels, double brightCoverage, int left, int top, bool flexible, bool diagnostics)
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
        if (!game.Find()) return 1;
        screen.Diagnostics = true;
        resets.EnsureGame();
        Console.WriteLine("Origin " + game.Origin + ", ACC client size " + game.Layout.ClientSize);
        Console.WriteLine("Practice pause: " + resets.Matches(game.Layout.Pause));
        Console.WriteLine("Quit confirmation: " + resets.Matches(game.Layout.Confirm));
        Console.WriteLine("Main menu: " + resets.Matches(game.Layout.Home));
        bool savedGames = resets.Matches(game.Layout.SavedTitle);
        Console.WriteLine("Saved Games: " + savedGames);
        if (savedGames)
        {
            for (int row = 0; row < VisibleSaveRows; row++)
                Console.WriteLine("Practice Play row " + (row + 1) + ": " + resets.MatchesSaveRow(row));
        }
        return 0;
    }

    private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    // Connects a reset to the rest of the engine: the window, save selection and the loop.
    private sealed class EngineResetHost : IResetHost
    {
        public bool IsClosing { get { return desktopSession != null && desktopSession.IsClosing; } }

        public void Publish(bool immediate)
        {
            if (desktopSession != null) desktopSession.Publish(immediate);
        }

        public Save SelectSave() { return RepeatCorner.SelectSave(); }

        public Save LoopSave { get { return loopSave; } }

        public void DisableLoop(string reason) { RepeatCorner.DisableLoop(reason); }

        public void Report(string message) { ReportStatus(message); }
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
