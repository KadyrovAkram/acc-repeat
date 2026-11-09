using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Dark controls for the Practice desk. Stock WinForms controls draw light chrome
// (dropdown arrows, spinners, disabled buttons) that cannot be themed.
internal static partial class RepeatCorner
{
    private static class Theme
    {
        public static readonly Color Background = Color.FromArgb(24, 25, 29);
        public static readonly Color Surface = Color.FromArgb(33, 35, 40);
        public static readonly Color Inset = Color.FromArgb(45, 47, 55);
        public static readonly Color Hover = Color.FromArgb(57, 60, 69);
        public static readonly Color RowHover = Color.FromArgb(38, 40, 46);
        public static readonly Color Line = Color.FromArgb(52, 54, 62);
        public static readonly Color Text = Color.FromArgb(244, 244, 248);
        public static readonly Color Muted = Color.FromArgb(163, 166, 178);
        public static readonly Color Faint = Color.FromArgb(108, 111, 123);
        public static readonly Color Red = Color.FromArgb(196, 38, 61);
        public static readonly Color RedHover = Color.FromArgb(218, 54, 78);
        public static readonly Color RedPressed = Color.FromArgb(160, 30, 50);
        public static readonly Color Green = Color.FromArgb(125, 225, 174);
        public static readonly Color Amber = Color.FromArgb(242, 178, 92);

        public static float Scale(Graphics graphics) { return graphics.DpiX / 96f; }

        public static Color Blend(Color first, Color second, float amount)
        {
            return Color.FromArgb(
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics graphics, Color color, RectangleF bounds, float radius)
        {
            using (var path = Rounded(bounds, radius))
            using (var brush = new SolidBrush(color))
                graphics.FillPath(brush, path);
        }

        public static void OutlineRounded(Graphics graphics, Color color, RectangleF bounds, float radius, float width)
        {
            using (var path = Rounded(bounds, radius))
            using (var pen = new Pen(color, width))
                graphics.DrawPath(pen, path);
        }

        public static Color ParentColor(Control control)
        {
            return control.Parent == null ? Background : control.Parent.BackColor;
        }

        // Windows 10 1809+ and 11: dark title bar; Windows 11: caption colour matching the header.
        public static void UseDarkTitleBar(Form form)
        {
            form.HandleCreated += (sender, args) =>
            {
                try
                {
                    int enabled = 1;
                    if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4) != 0)
                        DwmSetWindowAttribute(form.Handle, 19, ref enabled, 4);
                    int caption = Surface.R | (Surface.G << 8) | (Surface.B << 16);
                    DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    private sealed class PillButton : Button
    {
        private bool hover, pressed;
        private bool primary;

        public PillButton(string text)
        {
            Text = text;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 2, 6, 2);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool Primary
        {
            get { return primary; }
            set { if (primary != value) { primary = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { hover = pressed = false; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            float scale = Theme.Scale(graphics);
            graphics.Clear(Theme.ParentColor(this));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = 6 * scale;

            Color fill, text = Theme.Text;
            if (!Enabled)
            {
                fill = Theme.Blend(Theme.Inset, Theme.ParentColor(this), 0.5f);
                text = Theme.Faint;
            }
            else if (primary)
                fill = pressed ? Theme.RedPressed : hover ? Theme.RedHover : Theme.Red;
            else
                fill = pressed ? Theme.Inset : hover ? Theme.Hover : Theme.Inset;
            Theme.FillRounded(graphics, fill, bounds, radius);
            if (!Enabled)
                Theme.OutlineRounded(graphics, Theme.Line, bounds, radius, 1);
            if (Focused && ShowFocusCues)
                Theme.OutlineRounded(graphics, Theme.Text, bounds, radius, 1.5f * scale);

            TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, text, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private sealed class DarkPicker : ComboBox
    {
        private bool hover;

        public DarkPicker(string name)
        {
            AccessibleName = name;
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Inset;
            ForeColor = Theme.Text;
            Margin = new Padding(0, 2, 6, 2);
            Cursor = Cursors.Hand;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ItemHeight = Font.Height + Font.Height / 2;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemHeight = Font.Height + Font.Height / 2;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var fill = selected ? Theme.Hover : Theme.Inset;
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = e.Bounds;
            bounds.X += Font.Height / 2;
            bounds.Width -= Font.Height / 2;
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds, Theme.Text, fill,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && IsHandleCreated)
                using (var graphics = Graphics.FromHwnd(Handle))
                    PaintFace(graphics);
        }

        private void PaintFace(Graphics graphics)
        {
            float scale = Theme.Scale(graphics);
            graphics.Clear(Theme.ParentColor(this));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = 6 * scale;
            var fill = !Enabled ? Theme.ParentColor(this) : hover || DroppedDown ? Theme.Hover : Theme.Inset;
            Theme.FillRounded(graphics, fill, bounds, radius);
            if (!Enabled) Theme.OutlineRounded(graphics, Theme.Line, bounds, radius, 1);
            if (Focused) Theme.OutlineRounded(graphics, Theme.Muted, bounds, radius, 1.2f * scale);

            int inset = (int)(10 * scale), arrow = (int)(28 * scale);
            var textBounds = new Rectangle(inset, 0, Math.Max(0, Width - inset - arrow), Height);
            string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
            TextRenderer.DrawText(graphics, text, Font, textBounds, Enabled ? Theme.Text : Theme.Faint, fill,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            float centerX = Width - 15 * scale, centerY = Height / 2f;
            using (var pen = new Pen(Enabled ? Theme.Muted : Theme.Faint, 1.6f * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                graphics.DrawLines(pen, new[]
                {
                    new PointF(centerX - 4 * scale, centerY - 2 * scale),
                    new PointF(centerX, centerY + 2 * scale),
                    new PointF(centerX + 4 * scale, centerY - 2 * scale)
                });
            }
        }
    }

    // A compact "− 3 s +" control that replaces the light NumericUpDown spinner.
    private sealed class Stepper : TableLayoutPanel
    {
        private readonly PillButton down = new PillButton("−");
        private readonly PillButton up = new PillButton("+");
        private readonly Label value = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty,
            ForeColor = Theme.Text, UseMnemonic = false
        };
        private int current;
        public readonly int Minimum, Maximum;

        public Stepper(string name, int minimum, int maximum, int initial)
        {
            Minimum = minimum;
            Maximum = maximum;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            ColumnCount = 3;
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            down.AccessibleName = "Decrease " + name.ToLowerInvariant();
            up.AccessibleName = "Increase " + name.ToLowerInvariant();
            down.Margin = up.Margin = new Padding(0, 2, 0, 2);
            value.AccessibleName = name;
            Controls.Add(down, 0, 0);
            Controls.Add(value, 1, 0);
            Controls.Add(up, 2, 0);
            down.Click += (sender, args) => Value = current - 1;
            up.Click += (sender, args) => Value = current + 1;
            Value = initial;
        }

        public int Value
        {
            get { return current; }
            set
            {
                current = Math.Max(Minimum, Math.Min(Maximum, value));
                this.value.Text = current + " s";
                UpdateButtons();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            value.ForeColor = Enabled ? Theme.Text : Theme.Faint;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            down.Enabled = Enabled && current > Minimum;
            up.Enabled = Enabled && current < Maximum;
        }
    }

    // Recent runs with a bar showing how far each run was from the personal best. Hovering a
    // row shows a remove button; a best that dropped out of the recent list gets its own row.
    private sealed class AttemptList : Control
    {
        private Attempt[] runs = new Attempt[0], rows = new Attempt[0];
        private Attempt best;
        private int hoverRow = -1;
        private bool hoverRemove;
        private float paintScale = 1;
        public event Action<Attempt> RemoveRequested;

        public AttemptList()
        {
            AccessibleName = "Recent attempts";
            AccessibleRole = AccessibleRole.Table;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Margin = Padding.Empty;
        }

        public void SetRuns(Attempt[] recent, Attempt personalBest)
        {
            runs = recent;
            best = personalBest;
            bool bestListed = personalBest == null || recent.Any(IsPersonalBest);
            rows = bestListed ? recent : recent.Concat(new[] { personalBest }).ToArray();
            hoverRow = -1;
            var lines = new string[recent.Length];
            for (int index = 0; index < recent.Length; index++)
                lines[index] = string.Format("{0}: {1:0.000} seconds, {2}", RunLabel(index), recent[index].Seconds,
                    IsPersonalBest(recent[index]) ? "personal best" : DeltaText(recent[index]) + " delta to personal best");
            AccessibleDescription = string.Join("; ", lines);
            Invalidate();
        }

        private string RunLabel(int index)
        {
            return index >= runs.Length ? "Best" : index == 0 ? "Latest" : "Run " + (index + 1);
        }

        private int RowHeight { get { return (int)(38 * paintScale); } }
        private int HeaderHeight { get { return (int)(30 * paintScale); } }

        private RectangleF RemoveBox(int row)
        {
            float size = 26 * paintScale;
            return new RectangleF(Width - size - 6 * paintScale, HeaderHeight + row * RowHeight + (RowHeight - size) / 2f, size, size);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = e.Y < HeaderHeight ? -1 : (e.Y - HeaderHeight) / RowHeight;
            if (row >= rows.Length) row = -1;
            bool overRemove = row >= 0 && RemoveBox(row).Contains(e.Location);
            if (row != hoverRow || overRemove != hoverRemove)
            {
                hoverRow = row;
                hoverRemove = overRemove;
                Cursor = overRemove ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverRow = -1;
            hoverRemove = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && hoverRemove && hoverRow >= 0 && RemoveRequested != null)
                RemoveRequested(rows[hoverRow]);
        }

        private bool IsPersonalBest(Attempt run) { return best != null && run.Seconds == best.Seconds; }

        private string DeltaText(Attempt run)
        {
            return best == null ? "—" : string.Format("{0:+0.000;-0.000;0.000}", run.Seconds - best.Seconds);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            float scale = Theme.Scale(graphics);
            paintScale = scale;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int rowHeight = RowHeight, headerHeight = HeaderHeight;
            float[] widths = { 0.15f, 0.17f, 0.34f, 0.17f, 0.17f };
            int tableWidth = Math.Max(0, Width - (int)(RemoveBox(0).Width + 12 * scale));
            var columns = new int[widths.Length + 1];
            for (int index = 0; index < widths.Length; index++)
                columns[index + 1] = columns[index] + (int)(tableWidth * widths[index]);
            columns[widths.Length] = tableWidth;

            using (var small = new Font(Font.FontFamily, 8.5f))
            using (var bold = new Font(Font, FontStyle.Bold))
            using (var line = new Pen(Theme.Line))
            {
                string[] headings = { "RUN", "TIME", "DELTA", "ENTRY", "EXIT" };
                for (int index = 0; index < headings.Length; index++)
                    Cell(graphics, headings[index], small, Theme.Muted, columns, index, 0, headerHeight);
                graphics.DrawLine(line, 0, headerHeight - 1, Width, headerHeight - 1);

                double spread = 0.1;
                foreach (var run in runs)
                    if (best != null) spread = Math.Max(spread, run.Seconds - best.Seconds);

                for (int row = 0; row < rows.Length; row++)
                {
                    var run = rows[row];
                    int top = headerHeight + row * rowHeight;
                    bool personalBest = IsPersonalBest(run);
                    if (row == 0 || row == hoverRow)
                        using (var highlight = new SolidBrush(row == 0 && row != hoverRow ? Theme.Surface : Theme.RowHover))
                            graphics.FillRectangle(highlight, 0, top, Width, rowHeight - 1);
                    var color = personalBest ? Theme.Green : Theme.Text;
                    Cell(graphics, RunLabel(row), row == 0 ? bold : Font, row == 0 ? Theme.Text : Theme.Muted, columns, 0, top, rowHeight);
                    Cell(graphics, run.Seconds.ToString("0.000"), bold, color, columns, 1, top, rowHeight);
                    PaintDelta(graphics, run, personalBest, spread, scale, columns, top, rowHeight, small);
                    Cell(graphics, FormatSpeed(run.EntrySpeed), Font, Theme.Muted, columns, 3, top, rowHeight);
                    Cell(graphics, FormatSpeed(run.ExitSpeed), Font, Theme.Muted, columns, 4, top, rowHeight);
                    graphics.DrawLine(line, 0, top + rowHeight - 1, Width, top + rowHeight - 1);
                    if (row == hoverRow) PaintRemove(graphics, RemoveBox(row), scale);
                }
            }
        }

        private void PaintRemove(Graphics graphics, RectangleF box, float scale)
        {
            Theme.FillRounded(graphics, hoverRemove ? Theme.Red : Theme.Inset, box, 5 * scale);
            float inset = box.Width * 0.34f;
            using (var pen = new Pen(Theme.Text, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                graphics.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                graphics.DrawLine(pen, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
            }
        }

        private void PaintDelta(Graphics graphics, Attempt run, bool personalBest, double spread, float scale,
            int[] columns, int top, int height, Font small)
        {
            int left = columns[2], width = columns[3] - columns[2] - (int)(16 * scale);
            if (personalBest)
            {
                var tag = new RectangleF(left, top + height / 2f - 9 * scale, 30 * scale, 18 * scale);
                Theme.FillRounded(graphics, Color.FromArgb(48, Theme.Green), tag, 4 * scale);
                TextRenderer.DrawText(graphics, "PB", small, Rectangle.Round(tag), Theme.Green,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            if (best == null) return;
            double delta = run.Seconds - best.Seconds;
            int textWidth = (int)(62 * scale);
            Cell(graphics, DeltaText(run), Font, Theme.Amber, new[] { left, left + textWidth }, 0, top, height);
            float barWidth = Math.Max(3 * scale, (float)(delta / spread) * (width - textWidth));
            var bar = new RectangleF(left + textWidth, top + height / 2f - 3 * scale, barWidth, 6 * scale);
            Theme.FillRounded(graphics, Color.FromArgb(150, Theme.Amber), bar, 3 * scale);
        }

        private static void Cell(Graphics graphics, string text, Font font, Color color, int[] columns, int column, int top, int height)
        {
            var bounds = new Rectangle(columns[column], top, columns[column + 1] - columns[column], height);
            TextRenderer.DrawText(graphics, text, font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Shortcut reminders drawn as key caps.
    private sealed class KeyHints : Control
    {
        private readonly string[][] hints;

        public KeyHints(params string[][] hints)
        {
            this.hints = hints;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = Padding.Empty;
            var spoken = new string[hints.Length];
            for (int index = 0; index < hints.Length; index++) spoken[index] = hints[index][0] + " " + hints[index][1];
            AccessibleName = "Shortcuts";
            AccessibleDescription = string.Join(", ", spoken);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            float scale = Theme.Scale(graphics);
            graphics.Clear(Theme.ParentColor(this));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            using (var keyFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold))
            {
                float x = 0;
                int capHeight = (int)(22 * scale), padding = (int)(7 * scale);
                int top = (Height - capHeight) / 2;
                foreach (var hint in hints)
                {
                    var keySize = TextRenderer.MeasureText(graphics, hint[0], keyFont, Size.Empty, flags);
                    var cap = new RectangleF(x, top, keySize.Width + padding * 2, capHeight);
                    Theme.FillRounded(graphics, Theme.Inset, cap, 4 * scale);
                    TextRenderer.DrawText(graphics, hint[0], keyFont, Rectangle.Round(cap), Theme.Text, Theme.Inset,
                        flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x = cap.Right + 7 * scale;
                    var labelSize = TextRenderer.MeasureText(graphics, hint[1], Font, Size.Empty, flags);
                    TextRenderer.DrawText(graphics, hint[1], Font,
                        new Rectangle((int)x, 0, labelSize.Width + 2, Height), Theme.Muted, flags | TextFormatFlags.VerticalCenter);
                    x += labelSize.Width + 22 * scale;
                }
            }
        }
    }
}
