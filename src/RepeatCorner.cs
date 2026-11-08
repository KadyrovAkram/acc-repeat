using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

internal static partial class RepeatCorner
{
    private const string GameProcess = "AC2-Win64-Shipping";
    private const int HotkeyMessage = 0x0312;
    private static IntPtr game;
    private static Point origin;
    private static int scale;
    private static bool diagnostics;
    private static string stage;

    private static int Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length == 1 && args[0] == "--check-screen")
            return CheckScreen();

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

            try
            {
                Console.Title = "ACC Repeat Corner";
                Console.WriteLine("Ctrl+Shift+J: retry the newest Practice save while ACC is focused.");
                Console.WriteLine("Hold F9 to cancel a retry. Close this window to stop the tool.");
                Console.WriteLine("Prototype layout: 2880 x 1800 or 1440 x 900, English menus.");
                Message message;
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Id != HotkeyMessage || !FindGame())
                        continue;

                    try { Retry(); }
                    catch (Exception error) { Console.WriteLine("Stopped while " + stage + ": " + error.Message); }
                    while (PeekMessage(out message, IntPtr.Zero, HotkeyMessage, HotkeyMessage, 1)) { }
                }
            }
            finally { UnregisterHotKey(IntPtr.Zero, 1); }
        }
        return 0;
    }

    private static void Retry()
    {
        var elapsed = Stopwatch.StartNew();
        stage = "waiting for the shortcut keys to be released";
        while (IsDown(0x11) || IsDown(0x10) || IsDown(0x4A) || IsDown(0x5B) || IsDown(0x5C))
        {
            EnsureGame();
            if (elapsed.Elapsed.TotalSeconds > 2)
                throw new InvalidOperationException("Release the shortcut keys promptly and try again.");
            Thread.Sleep(20);
        }
        stage = "checking the practice save";
        var checkpoint = LatestPracticeSave();
        Console.WriteLine("Retrying " + checkpoint.Label + "...");
        int status = PracticeStatus();
        if (status != 2 && status != 3)
            throw new InvalidOperationException("Start in solo Free Practice, on the track.");
        if (status == 2 && !Matches(PracticeMenu))
        {
            stage = "opening the pause menu";
            EnsureGame();
            Key(0x1B, 0);
            Thread.Sleep(45);
            Key(0x1B, 2);
        }

        WaitFor("Free Practice pause menu", PracticeMenu, 3);
        stage = "selecting Quit";
        Click(719, 641);
        WaitFor("Quit confirmation", ConfirmQuit, 3);
        stage = "confirming Quit";
        Click(597, 514);
        WaitFor("main menu", HomeMenu, 45, HomeMenuHovered);
        stage = "opening Saved Games";
        Click(522, 638);
        WaitFor("Saved Games and its first Practice row", SavedGames, 6);
        var latest = LatestPracticeSave();
        if (checkpoint.File != latest.File || checkpoint.Created != latest.Created)
            throw new InvalidOperationException("The newest save changed during retry.");
        stage = "selecting Play on the newest save";
        Click(1099, 237, PlayButton);
        WaitFor("restored checkpoint and Drive button", PracticeMenu, 90);
        stage = "selecting Drive";
        Click(719, 366);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            if (!Matches(PracticeMenu) && PracticeStatus() == 2)
                break;
            if (watch.Elapsed.TotalSeconds > 3)
                throw new InvalidOperationException("The Drive menu did not close.");
            Thread.Sleep(100);
        }
        Console.WriteLine("Drive resumed after " + elapsed.Elapsed.TotalSeconds.ToString("0.0") + " seconds.");
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

    private sealed class Save
    {
        public string File, Created, Label, Mode;
    }

    private static Save LatestPracticeSave()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Assetto Corsa Competizione", "Savegames");
        if (!Directory.Exists(folder))
            throw new InvalidOperationException("Create a Free Practice save in ACC first.");
        var parser = new JavaScriptSerializer();
        var latest = Directory.EnumerateFiles(folder, "*H.json")
            .Where(path => Path.GetFileNameWithoutExtension(path).TrimEnd('H').All(char.IsDigit))
            .Select(path =>
            {
                var data = parser.DeserializeObject(File.ReadAllText(path)) as System.Collections.Generic.Dictionary<string, object>;
                if (data == null || !data.ContainsKey("creationDate") || !data.ContainsKey("gameMode"))
                    throw new InvalidOperationException("Could not read an ACC save header.");
                return new Save { File = path, Created = Convert.ToString(data["creationDate"]),
                    Mode = Convert.ToString(data["gameMode"]),
                    Label = Convert.ToString(data["trackDisplayName"]) + " (" + data["creationDate"] + ")" };
            })
            .OrderByDescending(save => save.Created, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latest == null || latest.Mode != "Practice")
            throw new InvalidOperationException("The newest saved game must be a Free Practice save.");
        return latest;
    }

    private static void WaitFor(string name, Signature[] signatures, int seconds, Signature[] alternative = null)
    {
        stage = "waiting for " + name;
        var watch = Stopwatch.StartNew();
        while (!Matches(signatures) && (alternative == null || !Matches(alternative)))
        {
            if (watch.Elapsed.TotalSeconds >= seconds)
                throw new InvalidOperationException("Could not recognize " + name + ".");
            Thread.Sleep(125);
        }
    }

    private static bool Matches(Signature[] signatures)
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
                captureGraphics.CopyFromScreen(origin.X + label.X * scale, origin.Y + label.Y * scale,
                    0, 0, capture.Size);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(capture, new Rectangle(0, 0, label.Width, label.Height));
                if (!label.Matches(bitmap))
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

        public bool Matches(Bitmap bitmap)
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
            return foregroundMatches >= foreground * 0.70 && backgroundMatches >= background * 0.78;
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
            throw new InvalidOperationException("ACC lost focus. Close any Windows menu, switch back to ACC, and retry.");
        Rect bounds;
        origin = Point.Empty;
        if (!GetClientRect(game, out bounds) || !ClientToScreen(game, ref origin) ||
            (bounds.Right != 1440 && bounds.Right != 2880) || bounds.Bottom != bounds.Right / 8 * 5)
            throw new InvalidOperationException("This prototype requires a 2880 x 1800 or 1440 x 900 ACC window. Detected " +
                bounds.Right + " x " + bounds.Bottom + ".");
        scale = bounds.Right / 1440;
    }

    private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    private static void Click(int x, int y, Signature[] hoveredButton = null)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 4)
        {
            EnsureGame();
            var point = new Point(origin.X + x * scale, origin.Y + y * scale);
            if (WindowFromPoint(point) != game)
                throw new InvalidOperationException("The target point is covered by another window.");
            if (!IsDown(0x01) && !IsDown(0x02) && SetCursorPos(point.X, point.Y))
            {
                // Give ACC a frame to update hover, which reveals Play on the first row.
                Thread.Sleep(65);
                if (hoveredButton == null || Matches(hoveredButton))
                {
                    EnsureGame();
                    Point cursor;
                    if (WindowFromPoint(point) == game && GetCursorPos(out cursor) &&
                        cursor == point && !IsDown(0x01) && !IsDown(0x02))
                    {
                        SendClick(point);
                        return;
                    }
                }
            }
            Thread.Sleep(125);
        }
        throw new InvalidOperationException("Could not select the ACC button. Leave the mouse still briefly and try again.");
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
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
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
