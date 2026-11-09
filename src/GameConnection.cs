using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

internal static partial class RepeatCorner
{
    // The ACC window as a reset sees it. GameConnection is the real one; tests use a simulated game.
    internal interface IGameWindow
    {
        // Where ACC's client area starts on screen and the menu layout for its size.
        // Both are read by RefreshGeometry.
        Point Origin { get; }
        MenuLayout Layout { get; }

        bool IsForeground { get; }
        bool IsOpen { get; }
        bool IsAt(Point screenPoint);

        // Reads the client area again. Picks the layout the first time after ForgetLayout and
        // throws if the window changed size since.
        void RefreshGeometry();
        void ForgetLayout();
        // Whether the window is back at the size the layout was chosen for (true when no layout is chosen yet).
        bool ClientSizeMatchesLayout();

        // ACC's game status from shared memory: 2 live, 3 paused, otherwise not in a session.
        int PracticeStatus();
    }

    // Owns the handle of the ACC window, where its client area is, which menu layout fits it,
    // and the reads from ACC's shared-memory telemetry.
    internal sealed class GameConnection : IGameWindow
    {
        private readonly IClock clock;
        private IntPtr handle;
        private Point origin;
        private MenuLayout layout;

        public GameConnection(IClock clock) { this.clock = clock; }

        public IntPtr Handle { get { return handle; } }
        public Point Origin { get { return origin; } }
        public MenuLayout Layout { get { return layout; } }
        public bool IsForeground { get { return GetForegroundWindow() == handle; } }
        public bool IsOpen { get { return IsWindow(handle); } }

        public bool IsAt(Point screenPoint) { return WindowFromPoint(screenPoint) == handle; }

        // Takes the foreground window as the game and says whether it is ACC.
        public bool Find()
        {
            handle = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(handle, out pid);
            if (pid == 0) return false;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                    return process.ProcessName == GameProcess;
            }
            catch (ArgumentException) { return false; }
        }

        public void ForgetLayout() { layout = null; }

        public void RefreshGeometry()
        {
            var size = ReadClientSize(out origin);
            if (layout == null)
            {
                layout = MenuLayout.Select(size);
                DiagnosticLog.Info(string.Format("ACC client area {0} x {1} at {2}, {3}.", size.Width, size.Height, origin.X, origin.Y));
            }
            else if (layout.ClientSize != size)
                throw new InvalidOperationException("The ACC window changed size. Reset again from the practice session.");
        }

        public bool ClientSizeMatchesLayout()
        {
            Point clientOrigin;
            return layout == null || ReadClientSize(out clientOrigin) == layout.ClientSize;
        }

        private Size ReadClientSize(out Point clientOrigin)
        {
            Rect bounds;
            clientOrigin = Point.Empty;
            if (!GetClientRect(handle, out bounds) || !ClientToScreen(handle, ref clientOrigin))
                throw new InvalidOperationException("Could not read the ACC window size and position.");
            return new Size(bounds.Right, bounds.Bottom);
        }

        public int PracticeStatus()
        {
            using (var map = MemoryMappedFile.OpenExisting(@"Local\acpmf_graphics", MemoryMappedFileRights.Read))
            using (var view = map.CreateViewAccessor(0, 12, MemoryMappedFileAccess.Read))
            {
                // The graphics page starts with the packet ID, game status, and session type.
                return view.ReadInt32(8) == 0 ? view.ReadInt32(4) : 0;
            }
        }

        private static MemoryMappedViewAccessor OpenPage(string name, int size)
        {
            using (var map = MemoryMappedFile.OpenExisting(@"Local\acpmf_" + name, MemoryMappedFileRights.Read))
                return map.CreateViewAccessor(0, size, MemoryMappedFileAccess.Read);
        }

        public TelemetryFrame ReadTelemetry(bool allowPaused)
        {
            try
            {
                // Field offsets are from Kunos's ACC Shared Memory v1.8.12 layout (4-byte packing).
                using (var graphics = OpenPage("graphics", 1240))
                using (var physics = OpenPage("physics", 32))
                using (var session = OpenPage("static", 688))
                {
                    int status = graphics.ReadInt32(4);
                    if ((status != 2 && !(allowPaused && status == 3)) || graphics.ReadInt32(8) != 0 ||
                        graphics.ReadInt32(160) != 0 || graphics.ReadInt32(1236) != 0 || session.ReadInt32(684) != 0)
                        return null;

                    byte[] track = new byte[66];
                    session.ReadArray(134, track, 0, track.Length);
                    string trackName = Encoding.Unicode.GetString(track).Split('\0')[0];
                    double position = graphics.ReadSingle(248);
                    double distance = graphics.ReadSingle(156);
                    double speed = physics.ReadSingle(28);
                    if (trackName.Length == 0 || !(position >= 0 && position <= 1) ||
                        !(distance >= 0 && distance < double.PositiveInfinity) || !(speed >= 0 && speed < 500))
                        return null;
                    // The player's car in carCoordinates, matched by ID; x and z are the ground plane.
                    int active = Math.Min(60, Math.Max(0, graphics.ReadInt32(252)));
                    int player = graphics.ReadInt32(1216), slot = -1;
                    for (int car = 0; car < active && slot < 0; car++)
                        if (graphics.ReadInt32(976 + car * 4) == player) slot = car;
                    return new TelemetryFrame
                    {
                        HasCoordinates = slot >= 0,
                        X = slot >= 0 ? graphics.ReadSingle(256 + slot * 12) : 0,
                        Z = slot >= 0 ? graphics.ReadSingle(264 + slot * 12) : 0,
                        Track = trackName, Position = position, Distance = distance, Speed = speed,
                        Packet = graphics.ReadInt32(0), Time = clock.Now, Paused = status == 3
                    };
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
        }
    }
}
