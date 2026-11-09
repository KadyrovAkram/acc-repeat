using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

// The three things a reset needs from Windows besides the ACC window: pixels from the
// screen, mouse and keyboard input, and time. Each sits behind a small interface so
// RetryController can be driven by a simulated game in the tests.
internal static partial class RepeatCorner
{
    internal interface IMenuScreen
    {
        // Whether every label in signatures is on screen, with the labels moved down by yOffset
        // reference pixels (used for the rows of the Saved Games list).
        bool Matches(MenuLayout layout, Point origin, Signature[] signatures, int yOffset, double brightCoverage);
    }

    internal interface IGameInput
    {
        bool IsDown(int key);
        bool MoveCursor(Point point);
        bool TryGetCursor(out Point point);
        // A left click at a screen point. Throws if Windows doesn't accept it.
        void Click(Point point);
        void Key(byte key, uint flags);
    }

    internal interface IClock
    {
        // Seconds on the engine clock.
        double Now { get; }
        void Sleep(int milliseconds);
    }

    // Copies the label regions of the ACC window from the screen and compares them with the signatures.
    internal sealed class ScreenCapture : IMenuScreen
    {
        // --check-screen prints the score of every label instead of stopping at the first miss.
        public bool Diagnostics;

        public bool Matches(MenuLayout layout, Point origin, Signature[] signatures, int yOffset, double brightCoverage)
        {
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
                    if (!label.Matches(bitmap, brightCoverage, padding, Diagnostics))
                    {
                        if (!Diagnostics) return false;
                        matched = false;
                    }
                }
            }
            return matched;
        }
    }

    // Synthetic mouse and keyboard input through user32.
    internal sealed class DesktopInput : IGameInput
    {
        public bool IsDown(int key) { return RepeatCorner.IsDown(key); }

        public bool MoveCursor(Point point) { return SetCursorPos(point.X, point.Y); }

        public bool TryGetCursor(out Point point) { return GetCursorPos(out point); }

        public void Key(byte key, uint flags) { RepeatCorner.Key(key, flags); }

        public void Click(Point point)
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
    }

    // The engine's stopwatch, shared with section timing so reload time and run times agree.
    internal sealed class EngineClock : IClock
    {
        public double Now { get { return sectionClock.Elapsed.TotalSeconds; } }

        public void Sleep(int milliseconds) { Thread.Sleep(milliseconds); }
    }
}
