using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A small click-through panel drawn over focused ACC practice sessions.
// Retry reads ACC's menus from the screen, so the overlay hides before every reload,
// is excluded from screen capture where Windows supports it, and never takes focus.
internal static partial class RepeatCorner
{
    private static readonly string[] OverlayPositions = { "Off", "Top centre", "Top left", "Top right", "Bottom centre" };
    private const string DefaultOverlayPosition = "Top centre";
    private static volatile IntPtr overlayHandle;
    private static volatile bool overlaySuppressed;

    private static bool IsPracticeSessionActive()
    {
        try
        {
            int status = game.PracticeStatus();
            return status == 2 || status == 3;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    // Engine thread: called as a reload starts, before any menu is captured.
    private static void HideOverlayNow()
    {
        overlaySuppressed = true;
        IntPtr handle = overlayHandle;
        if (handle != IntPtr.Zero) ShowWindowAsync(handle, 0);
    }

    // Engine thread: settings belong to the engine.
    private static void SetOverlayPosition(string position)
    {
        if (!OverlayPositions.Contains(position)) return;
        store.Settings.Overlay = position;
        store.Save();
        ReportStatus(position == "Off" ? "In-game overlay off." :
            "In-game overlay shown at the " + position.ToLowerInvariant() + " of the ACC window.", null);
    }

    private sealed class GameOverlay : NativeWindow, IDisposable
    {
        private IntPtr lastForeground;
        private bool foregroundIsGame;
        private string drawn;

        public GameOverlay()
        {
            const int Layered = 0x80000, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000, TopMost = 0x8;
            CreateHandle(new CreateParams
            {
                Caption = "ACC Repeat overlay",
                Style = unchecked((int)0x80000000),
                ExStyle = Layered | Transparent | ToolWindow | NoActivate | TopMost,
                Width = 1, Height = 1
            });
            try { SetWindowDisplayAffinity(Handle, 0x11); }
            catch (EntryPointNotFoundException) { }
            overlayHandle = Handle;
        }

        public void Dispose()
        {
            overlayHandle = IntPtr.Zero;
            if (Handle != IntPtr.Zero) DestroyHandle();
        }

        public void Update(DeskSnapshot view)
        {
            Rectangle client;
            if (view == null || view.Error != null || !view.InPracticeSession || view.Reloading || overlaySuppressed ||
                view.OverlayPosition == "Off" || !GameInFront(out client))
            {
                Hide();
                return;
            }

            var content = Describe(view);
            float scale = Math.Max(0.75f, Math.Min(2.5f, client.Height / 1080f));
            var size = new Size((int)(300 * scale), (int)(100 * scale));
            int margin = (int)(18 * scale);
            var position = new Point(client.Left + (client.Width - size.Width) / 2, client.Top + margin);
            if (view.OverlayPosition == "Top left") position.X = client.Left + margin;
            else if (view.OverlayPosition == "Top right") position.X = client.Right - size.Width - margin;
            else if (view.OverlayPosition == "Bottom centre") position.Y = client.Bottom - size.Height - margin;

            string key = string.Join("|", content) + "|" + position + "|" + size;
            if (key != drawn)
            {
                if (!Draw(content, position, size, scale))
                {
                    Hide();
                    return;
                }
                drawn = key;
            }

            if (overlaySuppressed || GetForegroundWindow() != lastForeground)
            {
                Hide();
                return;
            }

            // ACC can promote its own topmost window after the overlay is already visible.
            if (!IsWindowVisible(Handle) || GameIsAboveOverlay())
            {
                const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010, Show = 0x0040;
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, NoSize | NoMove | NoActivate | Show);
            }
        }

        private bool GameIsAboveOverlay()
        {
            const uint PreviousWindow = 3;
            // Bound the walk in case another app is rearranging the desktop z-order.
            var window = Handle;
            for (int index = 0; index < 64; index++)
            {
                window = GetWindow(window, PreviousWindow);
                if (window == IntPtr.Zero) return false;
                if (window == lastForeground) return true;
            }
            return false;
        }

        private void Hide()
        {
            if (IsWindowVisible(Handle)) ShowWindow(Handle, 0);
        }

        private bool GameInFront(out Rectangle client)
        {
            client = Rectangle.Empty;
            IntPtr foreground = GetForegroundWindow();
            if (foreground != lastForeground)
            {
                lastForeground = foreground;
                foregroundIsGame = false;
                uint pid;
                GetWindowThreadProcessId(foreground, out pid);
                if (pid != 0)
                {
                    try
                    {
                        using (var process = Process.GetProcessById((int)pid))
                            foregroundIsGame = process.ProcessName == GameProcess;
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
            }
            if (!foregroundIsGame || IsIconic(foreground)) return false;
            Rect bounds;
            var origin = Point.Empty;
            if (!GetClientRect(foreground, out bounds) || !ClientToScreen(foreground, ref origin)) return false;
            client = new Rectangle(origin.X, origin.Y, bounds.Right, bounds.Bottom);
            return client.Width >= 320 && client.Height >= 240;
        }

        // [title, value, delta, footer, title bar: "grey" | "red" | "green" | "yellow", value style: "time" | "text",
        //  delta colour, footer right]
        // The title bar follows ACC's HUD widgets and timing colours: red while a run is live or a
        // line is missing, green for a personal best, yellow for an invalid run.
        internal static string[] Describe(DeskSnapshot view)
        {
            string loop = view.Countdown.HasValue ? "Auto-reset in " + view.Countdown.Value + " s" :
                view.LoopArmed ? "Loop armed" : "";
            string personalBest = view.Best == null ? "" : "Best " + view.Best.Seconds.ToString("0.000");

            if (!view.Start.HasValue)
                return new[] { "TIMING LINES", "Mark start", "", "Ctrl+Shift+K just before braking", "red", "text", "", "" };
            if (!view.Finish.HasValue)
                return new[] { "TIMING LINES", "Mark finish", "", "Ctrl+Shift+L after the exit", "red", "text", "", "" };
            if (view.LiveStart.HasValue)
            {
                double elapsed = Math.Max(0, sectionClock.Elapsed.TotalSeconds - view.LiveStart.Value);
                return new[] { "LIVE", elapsed.ToString("0.0"), "", loop, "red", "time", "", personalBest };
            }
            if (view.LastInvalid != null)
                return new[] { "TRACK LIMITS", view.LastInvalid.Seconds.ToString("0.000"),
                    "INVALID", loop, "yellow", "time", "yellow", personalBest };

            var last = view.Recent.FirstOrDefault();
            if (last == null)
                return new[] { "READY", "—", "", loop.Length == 0 ? "Cross both timing lines to time a run" : loop,
                    "grey", "time", "", personalBest };

            bool isBest = view.Best != null && last.Seconds == view.Best.Seconds;
            string delta = "", deltaColour = "yellow";
            if (isBest)
            {
                delta = last.PreviousBestDelta.HasValue ? last.PreviousBestDelta.Value.ToString("+0.000;-0.000;0.000") : "PB";
                deltaColour = "green";
            }
            else if (view.Best != null)
                delta = (last.Seconds - view.Best.Seconds).ToString("+0.000;-0.000;0.000");
            return new[] { isBest ? "PERSONAL BEST" : "LAST RUN", last.Seconds.ToString("0.000"), delta, loop,
                isBest ? "green" : "grey", "time", deltaColour, isBest ? "" : personalBest };
        }

        private static Color Named(string colour)
        {
            return colour == "green" ? Theme.Green : colour == "yellow" ? Theme.Yellow :
                colour == "red" ? Theme.Red : Color.FromArgb(206, 206, 206);
        }

        private bool Draw(string[] content, Point position, Size size, float scale)
        {
            using (var bitmap = Paint(content, size, scale))
                return Push(bitmap, position);
        }

        internal static Bitmap Paint(string[] content, Size size, float scale)
        {
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    graphics.Clear(Color.Transparent);
                    // A 300 x 100 grid at scale 1: a 22 px title bar, 14 px side margins, and the main
                    // value and footer placed so the gaps above, between and below them are even.
                    float bar = 22 * scale;
                    using (var fill = new SolidBrush(Named(content[4]))) graphics.FillRectangle(fill, 0, 0, size.Width, bar);
                    using (var fill = new SolidBrush(Color.FromArgb(230, 10, 10, 10)))
                        graphics.FillRectangle(fill, 0, bar, size.Width, size.Height - bar);

                    float left = 14 * scale, right = size.Width - 14 * scale;
                    float valueBaseline = 64 * scale, footerBaseline = 87 * scale;
                    using (var caption = Theme.Label(13 * scale, GraphicsUnit.Pixel))
                    using (var value = Theme.Type((content[5] == "time" ? 40 : 28) * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var delta = Theme.Strong(22 * scale, GraphicsUnit.Pixel))
                    using (var footer = Theme.Type(13 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        float captionBaseline = (bar + Theme.CapHeight(caption, graphics)) / 2;
                        Theme.DrawInk(graphics, content[0], caption, content[4] == "red" ? Color.White : Color.FromArgb(14, 14, 14),
                            left, captionBaseline, false);
                        Theme.DrawInk(graphics, content[1], value, Theme.Text, left, valueBaseline, false);
                        Theme.DrawInk(graphics, content[2], delta, Named(content[6]), right, valueBaseline, true);
                        Theme.DrawInk(graphics, content[3], footer, content[3].StartsWith("Auto-reset") ? Theme.Yellow : Theme.Muted,
                            left, footerBaseline, false);
                        Theme.DrawInk(graphics, content[7], footer, Theme.Muted, right, footerBaseline, true);
                    }
                }
            }
            return bitmap;
        }

        private bool Push(Bitmap bitmap, Point position)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr image = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr previous = SelectObject(memory, image);
            try
            {
                var size = bitmap.Size;
                var source = Point.Empty;
                var blend = new BlendFunction { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
                return UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(image);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint direction);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr context);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point position, ref Size size,
        IntPtr source, ref Point sourcePosition, int key, ref BlendFunction blend, int flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr context);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr context);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr context, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
}
