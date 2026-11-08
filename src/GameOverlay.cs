using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A small click-through panel drawn over the ACC window while it is focused.
// Retry reads ACC's menus from the screen, so the overlay hides before every reload,
// is excluded from screen capture where Windows supports it, and never takes focus.
internal static partial class RepeatCorner
{
    private static readonly string[] OverlayPositions = { "Off", "Top centre", "Top left", "Top right", "Bottom centre" };
    private const string DefaultOverlayPosition = "Top centre";
    private static volatile IntPtr overlayHandle;
    private static volatile bool overlaySuppressed;

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
        settings.Overlay = position;
        StoreSettings();
        ReportStatus(position == "Off" ? "In-game overlay off." :
            "In-game overlay shown at the " + position.ToLowerInvariant() + " of the ACC window.");
    }

    private sealed class GameOverlay : NativeWindow, IDisposable
    {
        private IntPtr lastForeground;
        private bool foregroundIsGame, shown;
        private string drawn;

        public GameOverlay()
        {
            const int Layered = 0x80000, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000, TopMost = 0x8;
            CreateHandle(new CreateParams
            {
                Caption = "Repeat Corner overlay",
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
            if (view == null || view.Error != null || view.Reloading || overlaySuppressed ||
                view.OverlayPosition == "Off" || !GameInFront(out client))
            {
                Hide();
                return;
            }

            var content = Describe(view);
            float scale = Math.Max(0.75f, Math.Min(2.5f, client.Height / 1080f));
            var size = new Size((int)(300 * scale), (int)(86 * scale));
            int margin = (int)(18 * scale);
            var position = new Point(client.Left + (client.Width - size.Width) / 2, client.Top + margin);
            if (view.OverlayPosition == "Top left") position.X = client.Left + margin;
            else if (view.OverlayPosition == "Top right") position.X = client.Right - size.Width - margin;
            else if (view.OverlayPosition == "Bottom centre") position.Y = client.Bottom - size.Height - margin;

            string key = string.Join("|", content) + "|" + position + "|" + size;
            if (key != drawn)
            {
                Draw(content, position, size, scale);
                drawn = key;
            }
            if (!shown)
            {
                ShowWindow(Handle, 4);
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                shown = true;
            }
        }

        private void Hide()
        {
            if (!shown) return;
            ShowWindow(Handle, 0);
            shown = false;
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

        // [caption, value, delta, footer, accent: "red" | "green" | "amber", value style: "time" | "text", delta colour]
        internal static string[] Describe(DeskSnapshot view)
        {
            string loop = view.Countdown.HasValue ? "Auto-retry in " + view.Countdown.Value + " s" :
                view.LoopArmed ? "Loop armed" : null;
            string personalBest = view.Best == null ? null : "PB " + view.Best.Seconds.ToString("0.000");
            string footer = string.Join("   ·   ", new[] { loop, personalBest }.Where(part => part != null).ToArray());

            if (!view.Start.HasValue)
                return new[] { "TIMING", "Mark start", "", "Ctrl+Shift+K just before braking", "red", "text", "" };
            if (!view.Finish.HasValue)
                return new[] { "TIMING", "Mark finish", "", "Ctrl+Shift+L after the exit", "red", "text", "" };
            if (view.LiveStart.HasValue)
            {
                double elapsed = Math.Max(0, sectionClock.Elapsed.TotalSeconds - view.LiveStart.Value);
                return new[] { "LIVE", elapsed.ToString("0.0"), "", footer, "red", "time", "" };
            }
            if (view.LastCut != null)
                return new[] { "CUT · NOT COUNTED", view.LastCut.Seconds.ToString("0.000"),
                    "−" + view.LastCutShortBy.ToString("0") + " m", footer, "amber", "time", "amber" };

            var last = view.Recent.FirstOrDefault();
            if (last == null)
                return new[] { "READY", "—", "", footer.Length == 0 ? "Cross both markers to time a run" : footer, "red", "time", "" };

            bool isBest = view.Best != null && last.Seconds == view.Best.Seconds;
            string delta = "", deltaColour = "amber";
            if (isBest)
            {
                delta = last.PreviousBestDelta.HasValue ? last.PreviousBestDelta.Value.ToString("+0.000;-0.000;0.000") : "PB";
                deltaColour = "green";
            }
            else if (view.Best != null)
                delta = (last.Seconds - view.Best.Seconds).ToString("+0.000;-0.000;0.000");
            string accent = view.Countdown.HasValue ? "amber" : isBest ? "green" : "red";
            return new[] { "LAST RUN", last.Seconds.ToString("0.000"), delta, footer, accent, "time", deltaColour };
        }

        private static Color Named(string colour)
        {
            return colour == "green" ? Theme.Green : colour == "amber" ? Theme.Amber : Theme.Red;
        }

        private void Draw(string[] content, Point position, Size size, float scale)
        {
            using (var bitmap = Paint(content, size, scale))
                Push(bitmap, position);
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
                    var bounds = new RectangleF(0, 0, size.Width - 1, size.Height - 1);
                    using (var panel = Theme.Rounded(bounds, 10 * scale))
                    {
                        using (var fill = new SolidBrush(Color.FromArgb(228, 18, 19, 23)))
                            graphics.FillPath(fill, panel);
                        graphics.SetClip(panel);
                        using (var accent = new SolidBrush(Named(content[4])))
                            graphics.FillRectangle(accent, 0, 0, 4 * scale, size.Height);
                        graphics.ResetClip();
                    }

                    float left = 18 * scale, right = size.Width - 16 * scale;
                    using (var caption = new Font("Segoe UI Semibold", 11 * scale, GraphicsUnit.Pixel))
                    using (var value = content[5] == "time" ? new Font("Segoe UI", 34 * scale, FontStyle.Bold, GraphicsUnit.Pixel) :
                        new Font("Segoe UI", 24 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var delta = new Font("Segoe UI", 20 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var footer = new Font("Segoe UI", 12.5f * scale, GraphicsUnit.Pixel))
                    using (var muted = new SolidBrush(Theme.Muted))
                    using (var text = new SolidBrush(Theme.Text))
                    using (var nearRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    using (var noWrap = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                    {
                        graphics.DrawString(content[0], caption, muted, left, 8 * scale);
                        float valueTop = content[5] == "time" ? 18 * scale : 24 * scale;
                        graphics.DrawString(content[1], value, text, left - 2 * scale, valueTop);
                        if (content[2].Length != 0)
                            using (var deltaBrush = new SolidBrush(Named(content[6])))
                                graphics.DrawString(content[2], delta, deltaBrush,
                                    new RectangleF(left, 18 * scale, right - left, 44 * scale), nearRight);
                        var footerColour = content[3].StartsWith("Auto-retry") ? Theme.Amber :
                            content[3].StartsWith("Loop armed") ? Theme.Green : Theme.Muted;
                        using (var footerBrush = new SolidBrush(footerColour))
                            graphics.DrawString(content[3], footer, footerBrush,
                                new RectangleF(left, 62 * scale, right - left, 20 * scale), noWrap);
                    }
                }
            }
            return bitmap;
        }

        private void Push(Bitmap bitmap, Point position)
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
                UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, 2);
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
