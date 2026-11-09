using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

// Place the timing markers by clicking on the track map.
internal static partial class RepeatCorner
{
    private sealed class MarkerMapDialog : Form
    {
        private readonly DesktopSession session;
        private readonly string track;
        private readonly MapCanvas canvas = new MapCanvas { Dock = DockStyle.Fill };
        private readonly PillButton importMap = new PillButton("Import map…");
        private readonly PillButton placeStart = new PillButton("Start");
        private readonly PillButton placeFinish = new PillButton("Finish");
        private readonly PillButton save = new PillButton("Save lines") { Primary = true };
        private readonly Label positions = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true };
        private readonly Label loadLegend = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Amber, AutoEllipsis = true
        };
        private readonly Timer refresh = new Timer { Interval = 500 };
        private readonly double? originalStart, originalFinish;
        private bool placingStart = true;

        public MarkerMapDialog(DesktopSession session, DeskSnapshot view, Font font)
        {
            this.session = session;
            track = view.Track;
            originalStart = canvas.Start = view.Start;
            originalFinish = canvas.Finish = view.Finish;
            canvas.Map = view.Map;
            ShowLoadPosition(view.LoadPosition);

            Text = "Timing lines · " + (view.TrackName ?? track);
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = font;
            ClientSize = new Size(820, 620);
            MinimumSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Theme.UseDarkTitleBar(this);
            SuspendLayout();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(16, 10, 10, 10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            var prompt = new Label
            {
                Text = "Click the track to place the", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.Muted, Margin = new Padding(0, 0, 8, 0), UseMnemonic = false
            };
            top.Controls.Add(importMap, 0, 0);
            top.Controls.Add(prompt, 1, 0);
            top.Controls.Add(placeStart, 2, 0);
            top.Controls.Add(placeFinish, 3, 0);

            var mapTools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = Padding.Empty };
            mapTools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mapTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            mapTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            mapTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            mapTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mapTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            var zoomOut = new PillButton("−") { AccessibleName = "Zoom out" };
            var zoomIn = new PillButton("+") { AccessibleName = "Zoom in" };
            var fitTrack = new PillButton("Fit") { AccessibleName = "Fit whole track" };
            var mapHelp = new Label
            {
                Text = "Scroll to zoom · Right-drag to pan", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Muted, Margin = new Padding(6, 0, 0, 0), UseMnemonic = false, AutoEllipsis = true
            };
            mapTools.Controls.Add(zoomOut, 0, 0);
            mapTools.Controls.Add(zoomIn, 1, 0);
            mapTools.Controls.Add(fitTrack, 2, 0);
            mapTools.Controls.Add(mapHelp, 3, 0);
            mapTools.Controls.Add(loadLegend, 4, 0);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            var cancel = new PillButton("Cancel") { DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(positions, 0, 0);
            bottom.Controls.Add(cancel, 1, 0);
            bottom.Controls.Add(save, 2, 0);

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(mapTools, 0, 1);
            root.Controls.Add(canvas, 0, 2);
            root.Controls.Add(bottom, 0, 3);
            Controls.Add(root);
            CancelButton = cancel;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);

            placeStart.Click += (sender, args) => SetMode(true);
            placeFinish.Click += (sender, args) => SetMode(false);
            importMap.Click += ImportMap;
            zoomOut.Click += (sender, args) => canvas.ZoomBy(-1);
            zoomIn.Click += (sender, args) => canvas.ZoomBy(1);
            fitTrack.Click += (sender, args) => canvas.ResetZoom();
            canvas.Picked += Place;
            save.Click += Save;
            refresh.Tick += (sender, args) =>
            {
                var latest = session.Snapshot;
                if (latest != null && SameTrack(latest.Track, track) &&
                    (latest.Map != canvas.Map || latest.LoadPosition != canvas.LoadPosition))
                {
                    canvas.Map = latest.Map;
                    ShowLoadPosition(latest.LoadPosition);
                    canvas.Invalidate();
                }
            };
            refresh.Start();
            SetMode(!canvas.Start.HasValue || canvas.Finish.HasValue);
        }

        private void ShowLoadPosition(double? position)
        {
            canvas.LoadPosition = position;
            loadLegend.Text = position.HasValue ? string.Format("◆ Load {0:0.00%}", position.Value) : "Load position unavailable";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // WinForms can send the wheel event to the focused button's parent.
            canvas.ZoomFromWheel(e, canvas.PointToClient(PointToScreen(e.Location)));
            base.OnMouseWheel(e);
        }

        private async void ImportMap(object sender, EventArgs args)
        {
            string path;
            using (var picker = new OpenFileDialog
            {
                Title = "Import " + track + ".bin",
                Filter = "Race Element ACC maps (*.bin)|*.bin",
                FileName = track + ".bin",
                RestoreDirectory = true
            })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                path = picker.FileName;
            }

            importMap.Enabled = false;
            var pending = new TaskCompletionSource<TrackMapView>();
            session.Post(() =>
            {
                try
                {
                    var map = ImportTrackMap(track, path);
                    ReportStatus("Imported the track map for " + track + ".");
                    pending.SetResult(map);
                }
                catch (Exception error) { pending.SetException(error); }
            });
            try
            {
                var map = await pending.Task;
                if (IsDisposed) return;
                canvas.Map = map;
                canvas.Invalidate();
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                    MessageBox.Show(this, error.Message, "Could not import track map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed) importMap.Enabled = true;
            }
        }

        private void SetMode(bool start)
        {
            placingStart = start;
            placeStart.Primary = start;
            placeFinish.Primary = !start;
            canvas.PlacingStart = start;
            UpdateSummary();
        }

        private void Place(double position)
        {
            if (placingStart)
            {
                canvas.Start = position;
                SetMode(false);
            }
            else
            {
                canvas.Finish = position;
                UpdateSummary();
            }
            canvas.Invalidate();
        }

        private string Problem()
        {
            if (!canvas.Start.HasValue || !canvas.Finish.HasValue) return "Place both timing lines.";
            double span = SectionTimer.ForwardDistance(canvas.Start.Value, canvas.Finish.Value);
            if (span < 0.0002 || span >= 0.5) return "The finish must come after the start, within half a lap.";
            return null;
        }

        private void UpdateSummary()
        {
            string problem = Problem();
            bool changed = canvas.Start != originalStart || canvas.Finish != originalFinish;
            positions.Text = problem ?? string.Format("Start {0:0.00%}   ·   Finish {1:0.00%}{2}", canvas.Start.Value,
                canvas.Finish.Value, changed ? "   ·   saving starts a new run history" : "");
            positions.ForeColor = problem == null ? Theme.Muted : Theme.Amber;
            save.Enabled = problem == null && changed;
        }

        private void Save(object sender, EventArgs args)
        {
            if (Problem() != null) return;
            double start = canvas.Start.Value, finish = canvas.Finish.Value;
            string name = track;
            session.Post(() => ApplyMarkers(name, start, finish,
                string.Format("Timing lines placed on the map: start {0:0.00%}, finish {1:0.00%}.", start, finish)));
            DialogResult = DialogResult.OK;
        }
    }

    private sealed class MapCanvas : Control
    {
        public TrackMapView Map;
        public double? Start { get; set; }
        public double? Finish { get; set; }
        public double? LoadPosition { get; set; }
        public bool PlacingStart = true;
        public event Action<double> Picked;
        private const double ZoomStep = 1.25, MaxZoom = 12;
        private double zoom = 1;
        private PointF? zoomCenter;
        private PointF worldCenter;
        private MouseButtons panButton;
        private Point lastPanPoint;
        private double? hover;
        private float scale;

        private PointF ViewCenter { get { return new PointF(Width / 2f, Height / 2f); } }

        public MapCanvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Margin = new Padding(0, 6, 6, 6);
            Cursor = Cursors.Cross;
            AccessibleName = "Track map";
            AccessibleRole = AccessibleRole.Graphic;
        }

        private bool UpdateTransform(float pad)
        {
            if (Map == null || Map.IsEmpty) return false;
            double maxX = double.MinValue, maxZ = double.MinValue;
            double minX = double.MaxValue, minZ = double.MaxValue;
            for (int index = 0; index < MapBins; index++)
            {
                if (!Map.Filled[index]) continue;
                minX = Math.Min(minX, Map.X[index]); maxX = Math.Max(maxX, Map.X[index]);
                minZ = Math.Min(minZ, Map.Z[index]); maxZ = Math.Max(maxZ, Map.Z[index]);
            }
            double width = Math.Max(1, maxX - minX), depth = Math.Max(1, maxZ - minZ);
            scale = (float)(Math.Min((Width - 2 * pad) / width, (Height - 2 * pad) / depth) * zoom);
            worldCenter = zoomCenter ?? new PointF((float)(minX + width / 2), (float)(minZ + depth / 2));
            return scale > 0;
        }

        private PointF ToScreen(PointF world)
        {
            var center = ViewCenter;
            return new PointF(center.X + (world.X - worldCenter.X) * scale, center.Y + (world.Y - worldCenter.Y) * scale);
        }

        private PointF ToWorld(PointF screen)
        {
            var center = ViewCenter;
            return new PointF(worldCenter.X + (screen.X - center.X) / scale, worldCenter.Y + (screen.Y - center.Y) / scale);
        }

        public void ZoomBy(double steps, PointF? location = null)
        {
            if (!UpdateTransform(36 * DeviceScale())) return;
            double next = Math.Max(1, Math.Min(MaxZoom, zoom * Math.Pow(ZoomStep, steps)));
            if (next <= 1)
            {
                ResetZoom();
                return;
            }
            if (next == zoom) return;

            var anchor = location ?? ViewCenter;
            var world = ToWorld(anchor);
            var center = ViewCenter;
            float nextScale = (float)(scale * next / zoom);
            zoomCenter = new PointF(world.X - (anchor.X - center.X) / nextScale,
                world.Y - (anchor.Y - center.Y) / nextScale);
            zoom = next;
            hover = null;
            Invalidate();
        }

        public void ResetZoom()
        {
            zoom = 1;
            zoomCenter = null;
            hover = null;
            Invalidate();
        }

        public void ZoomFromWheel(MouseEventArgs e, Point location)
        {
            if (!ClientRectangle.Contains(location) || Map == null || Map.IsEmpty) return;
            ZoomBy(e.Delta / (double)SystemInformation.MouseWheelScrollDelta, location);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomFromWheel(e, e.Location);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (zoom == 1 || (e.Button != MouseButtons.Right && e.Button != MouseButtons.Middle)) return;
            panButton = e.Button;
            lastPanPoint = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
            hover = null;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == panButton) Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture || panButton == MouseButtons.None) return;
            panButton = MouseButtons.None;
            Cursor = Cursors.Cross;
        }

        private double? MarkerAt(Point location)
        {
            float dpi = DeviceScale();
            if (!UpdateTransform(36 * dpi)) return null;
            var world = ToWorld(location);
            double gap;
            var nearest = Map.Nearest(world.X, world.Y, out gap);
            return gap * scale < 28 * dpi ? nearest : null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (panButton != MouseButtons.None)
            {
                if (UpdateTransform(36 * DeviceScale()))
                {
                    var center = ViewCenter;
                    zoomCenter = ToWorld(new PointF(center.X + lastPanPoint.X - e.X, center.Y + lastPanPoint.Y - e.Y));
                    lastPanPoint = e.Location;
                    Invalidate();
                }
                return;
            }
            double? next = MarkerAt(e.Location);
            if (next != hover)
            {
                hover = next;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || panButton != MouseButtons.None || Picked == null) return;
            var position = MarkerAt(e.Location);
            if (position.HasValue) Picked(position.Value);
        }

        private float DeviceScale()
        {
            using (var graphics = CreateGraphics()) return graphics.DpiX / 96f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            float dpi = Theme.Scale(graphics);
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (!UpdateTransform(36 * dpi))
            {
                string message = "No map of " + (Map == null ? "this track" : Map.Track) + " yet.\n\n" +
                    "Import a map file or drive a lap with ACC Repeat open.";
                TextRenderer.DrawText(graphics, message, Font, ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            using (var road = new Pen(Theme.Inset, 10 * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var centre = new Pen(Theme.Faint, 1.2f * dpi))
                foreach (var run in Runs())
                {
                    if (run.Length < 2) continue;
                    graphics.DrawLines(road, run);
                    graphics.DrawLines(centre, run);
                }

            if (Start.HasValue && Finish.HasValue)
            {
                var section = Path(Start.Value, Finish.Value);
                if (section.Length >= 2)
                    using (var pen = new Pen(Color.FromArgb(220, Theme.Red), 5 * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        graphics.DrawLines(pen, section);
            }
            DrawDirection(graphics, dpi);
            DrawLoadPosition(graphics, dpi);
            if (Start.HasValue) DrawMarker(graphics, Start.Value, Theme.Green, "S", dpi);
            if (Finish.HasValue) DrawMarker(graphics, Finish.Value, Theme.Text, "F", dpi);
            if (hover.HasValue)
            {
                var point = Map.PointAt(hover.Value);
                if (point.HasValue)
                {
                    var at = ToScreen(point.Value);
                    var colour = PlacingStart ? Theme.Green : Theme.Text;
                    using (var ring = new Pen(colour, 2 * dpi)) graphics.DrawEllipse(ring, at.X - 9 * dpi, at.Y - 9 * dpi, 18 * dpi, 18 * dpi);
                    string label = (PlacingStart ? "Start " : "Finish ") + hover.Value.ToString("0.00%");
                    var size = TextRenderer.MeasureText(graphics, label, Font);
                    float boxWidth = size.Width + 10 * dpi, boxHeight = size.Height + 6 * dpi;
                    var box = new RectangleF(at.X - boxWidth / 2, at.Y - 18 * dpi - boxHeight, boxWidth, boxHeight);
                    if (box.Top < 0) box.Y = at.Y + 18 * dpi;
                    box.X = Math.Max(2, Math.Min(Width - boxWidth - 2, box.X));
                    Theme.FillRounded(graphics, Theme.Surface, box, 4 * dpi);
                    TextRenderer.DrawText(graphics, label, Font, Rectangle.Round(box), colour, Theme.Surface,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            if (Map.Coverage < 0.98)
                TextRenderer.DrawText(graphics, string.Format("Map {0:0%} recorded. Drive a full lap to complete it.", Map.Coverage),
                    Font, new Rectangle(8, Height - (int)(30 * dpi), Width - 16, (int)(26 * dpi)), Theme.Amber,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        // Connected stretches of recorded points, broken where the map has gaps.
        private IEnumerable<PointF[]> Runs()
        {
            int first = 0;
            while (first < MapBins && Map.Filled[first]) first++;
            if (first == MapBins)
            {
                var loop = new PointF[MapBins + 1];
                for (int index = 0; index <= MapBins; index++)
                    loop[index] = ToScreen(new PointF((float)Map.X[index % MapBins], (float)Map.Z[index % MapBins]));
                yield return loop;
                yield break;
            }
            var run = new List<PointF>();
            for (int step = 1; step <= MapBins; step++)
            {
                int index = (first + step) % MapBins;
                if (Map.Filled[index]) run.Add(ToScreen(new PointF((float)Map.X[index], (float)Map.Z[index])));
                else if (run.Count != 0)
                {
                    yield return run.ToArray();
                    run.Clear();
                }
            }
            if (run.Count != 0) yield return run.ToArray();
        }

        private PointF[] Path(double from, double to)
        {
            var points = new List<PointF>();
            double span = SectionTimer.ForwardDistance(from, to);
            int steps = Math.Max(2, (int)(span * MapBins) + 1);
            for (int step = 0; step <= steps; step++)
            {
                var point = Map.PointAt(from + span * step / steps);
                if (point.HasValue) points.Add(ToScreen(point.Value));
            }
            return points.ToArray();
        }

        // A small arrow showing which way the lap runs, near the start of the section.
        private void DrawDirection(Graphics graphics, float dpi)
        {
            double from = Start ?? 0;
            var a = Map.PointAt(from + 0.006);
            var b = Map.PointAt(from + 0.012);
            if (!a.HasValue || !b.HasValue) return;
            var head = ToScreen(b.Value);
            var tail = ToScreen(a.Value);
            float dx = head.X - tail.X, dy = head.Y - tail.Y, length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1) return;
            dx /= length; dy /= length;
            float size = 7 * dpi;
            using (var brush = new SolidBrush(Theme.Muted))
                graphics.FillPolygon(brush, new[]
                {
                    new PointF(head.X + dx * size, head.Y + dy * size),
                    new PointF(head.X - dx * size - dy * size, head.Y - dy * size + dx * size),
                    new PointF(head.X - dx * size + dy * size, head.Y - dy * size - dx * size)
                });
        }

        private void DrawLoadPosition(Graphics graphics, float dpi)
        {
            if (!LoadPosition.HasValue) return;
            var point = Map.PointAt(LoadPosition.Value, requireAdjacentPoints: true);
            if (!point.HasValue) return;
            var at = ToScreen(point.Value);
            float radius = 10 * dpi;
            var diamond = new[]
            {
                new PointF(at.X, at.Y - radius), new PointF(at.X + radius, at.Y),
                new PointF(at.X, at.Y + radius), new PointF(at.X - radius, at.Y)
            };
            using (var fill = new SolidBrush(Theme.Amber)) graphics.FillPolygon(fill, diamond);
            using (var edge = new Pen(Theme.Background, 2 * dpi)) graphics.DrawPolygon(edge, diamond);
        }

        private void DrawMarker(Graphics graphics, double position, Color colour, string letter, float dpi)
        {
            var point = Map.PointAt(position);
            if (!point.HasValue) return;
            var at = ToScreen(point.Value);
            float radius = 11 * dpi;
            var circle = new RectangleF(at.X - radius, at.Y - radius, radius * 2, radius * 2);
            using (var fill = new SolidBrush(colour)) graphics.FillEllipse(fill, circle);
            using (var edge = new Pen(Theme.Background, 2 * dpi)) graphics.DrawEllipse(edge, circle);
            using (var bold = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(graphics, letter, bold, Rectangle.Round(circle), Theme.Background,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}
