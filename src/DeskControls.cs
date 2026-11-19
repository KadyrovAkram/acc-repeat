using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Dark controls for the Practice desk. Stock WinForms controls draw light chrome
// (dropdown arrows, spinners, disabled buttons) that cannot be themed.
// The look follows ACC's own screens and timing displays: flat black panels, square
// corners, condensed capitals for labels, and colour only where it means something.
internal static partial class RepeatCorner
{
    private static class Theme
    {
        public static readonly Color Background = Color.FromArgb(14, 14, 14);
        public static readonly Color Surface = Color.FromArgb(24, 24, 24);
        public static readonly Color Inset = Color.FromArgb(40, 40, 40);
        public static readonly Color Hover = Color.FromArgb(56, 56, 56);
        public static readonly Color RowHover = Color.FromArgb(30, 30, 30);
        public static readonly Color Line = Color.FromArgb(44, 44, 44);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Muted = Color.FromArgb(150, 150, 150);
        public static readonly Color Faint = Color.FromArgb(96, 96, 96);
        // ACC's menu highlight red: primary actions and the timed section on the map.
        public static readonly Color Red = Color.FromArgb(214, 0, 0);
        public static readonly Color RedHover = Color.FromArgb(236, 26, 26);
        public static readonly Color RedPressed = Color.FromArgb(166, 0, 0);
        // Timing colours: green is a personal best, yellow is slower or needs attention.
        public static readonly Color Green = Color.FromArgb(40, 204, 92);
        public static readonly Color Yellow = Color.FromArgb(245, 196, 0);

        // Bahnschrift (DIN-style, ships with Windows 10 1709+) has the condensed widths and
        // tabular figures a timing display wants; older Windows falls back to Segoe UI.
        private static readonly bool hasBahnschrift =
            new InstalledFontCollection().Families.Any(family => family.Name == "Bahnschrift");

        public static Font Type(float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return new Font(hasBahnschrift ? "Bahnschrift" : "Segoe UI", size, style, unit);
        }

        public static Font Strong(float size, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return new Font(hasBahnschrift ? "Bahnschrift SemiBold" : "Segoe UI Semibold", size, FontStyle.Regular, unit);
        }

        // Condensed capitals for labels and buttons, as in ACC's menus.
        public static Font Label(float size, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return new Font(hasBahnschrift ? "Bahnschrift SemiBold Condensed" : "Segoe UI Semibold", size, FontStyle.Regular, unit);
        }

        public static float Scale(Graphics graphics) { return graphics.DpiX / 96f; }

        // Draws text as outlines so its ink, not its text box, starts (or ends) exactly at x and
        // sits on the given baseline. Large type otherwise drifts inward by its side bearing.
        public static void DrawInk(Graphics graphics, string text, Font font, Color color, float x, float baseline, bool alignRight)
        {
            if (string.IsNullOrEmpty(text)) return;
            var family = font.FontFamily;
            float size = font.Unit == GraphicsUnit.Pixel ? font.Size : font.SizeInPoints * graphics.DpiY / 72f;
            // Winding fill: Bahnschrift is a variable font whose outlines overlap, and the default
            // alternate fill would punch holes where they cross.
            using (var path = new GraphicsPath(FillMode.Winding))
            using (var brush = new SolidBrush(color))
            {
                path.AddString(text, family, (int)font.Style, size, PointF.Empty, StringFormat.GenericTypographic);
                var ink = path.GetBounds();
                float ascent = size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
                using (var move = new Matrix())
                {
                    move.Translate(alignRight ? x - ink.Right : x - ink.Left, baseline - ascent);
                    path.Transform(move);
                }
                var smoothing = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPath(brush, path);
                graphics.SmoothingMode = smoothing;
            }
        }

        // Height of capital letters above the baseline, for centring a line of type by its caps.
        public static float CapHeight(Font font, Graphics graphics)
        {
            float size = font.Unit == GraphicsUnit.Pixel ? font.Size : font.SizeInPoints * graphics.DpiY / 72f;
            using (var path = new GraphicsPath())
            {
                path.AddString("H", font.FontFamily, (int)font.Style, size, PointF.Empty, StringFormat.GenericTypographic);
                return path.GetBounds().Height;
            }
        }

        public static Color Blend(Color first, Color second, float amount)
        {
            return Color.FromArgb(
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        public static void Fill(Graphics graphics, Color color, RectangleF bounds)
        {
            using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, bounds);
        }

        public static void Outline(Graphics graphics, Color color, Rectangle bounds, float width)
        {
            using (var pen = new Pen(color, width) { Alignment = PenAlignment.Inset })
                graphics.DrawRectangle(pen, bounds);
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
                    int caption = Background.R | (Background.G << 8) | (Background.B << 16);
                    DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    // A single line of text with no side padding, so it lines up with other text and controls.
    // Large type is drawn by its ink and sits on a baseline near the bottom edge.
    private sealed class FlushLabel : Control
    {
        public FlushLabel(float size, bool strong)
        {
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Font = strong ? Theme.Strong(size) : Theme.Type(size);
            ForeColor = Theme.Text;
            AccessibleRole = AccessibleRole.StaticText;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        // For large type: the gap between the baseline and the bottom edge, in 96 dpi pixels.
        public int BaselineInset { get; set; }

        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }
        protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            var back = Theme.ParentColor(this);
            graphics.Clear(back);
            if (BaselineInset > 0)
                Theme.DrawInk(graphics, Text, Font, ForeColor, 0, Height - BaselineInset * Theme.Scale(graphics), false);
            else
                TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ForeColor, back,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // "● Connected to ACC", right-aligned: a green dot when connected, a hollow ring while waiting.
    private sealed class ConnectionBadge : Control
    {
        private bool connected;

        public ConnectionBadge()
        {
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Font = Theme.Type(10.5f);
            AccessibleRole = AccessibleRole.StaticText;
            Connected = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool Connected
        {
            get { return connected; }
            set
            {
                connected = value;
                Text = AccessibleName = value ? "Connected to ACC" : "Waiting for ACC";
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            float scale = Theme.Scale(graphics);
            var back = Theme.ParentColor(this);
            graphics.Clear(back);
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
            var size = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, flags);
            int textLeft = Width - size.Width;
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(textLeft, 0, size.Width, Height),
                connected ? Theme.Text : Theme.Muted, back, flags);
            float diameter = 8 * scale;
            var dot = new RectangleF(textLeft - diameter - 8 * scale, (Height - diameter) / 2f + 0.5f * scale, diameter, diameter);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (connected)
                using (var brush = new SolidBrush(Theme.Green)) graphics.FillEllipse(brush, dot);
            else
                using (var pen = new Pen(Theme.Muted, 1.4f * scale)) graphics.DrawEllipse(pen, dot);
        }
    }

    // A flat, square button.
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
            Font = Theme.Type(10.5f);
            ForeColor = Color.FromArgb(232, 232, 232);
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

            Color fill, text = ForeColor;
            if (!Enabled)
            {
                fill = Theme.Blend(Theme.Inset, Theme.ParentColor(this), 0.55f);
                text = Theme.Faint;
            }
            else if (primary)
                fill = pressed ? Theme.RedPressed : hover ? Theme.RedHover : Theme.Red;
            else
                fill = pressed ? Theme.Inset : hover ? Theme.Hover : Theme.Inset;
            Theme.Fill(graphics, fill, ClientRectangle);
            if (Focused && ShowFocusCues)
                Theme.Outline(graphics, Theme.Text, ClientRectangle, 1.5f * scale);

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
            var fill = selected ? Theme.Red : Theme.Inset;
            Theme.Fill(e.Graphics, fill, e.Bounds);
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
            var fill = !Enabled ? Theme.Blend(Theme.Inset, Theme.ParentColor(this), 0.55f) :
                hover || DroppedDown ? Theme.Hover : Theme.Inset;
            Theme.Fill(graphics, fill, ClientRectangle);
            if (Focused) Theme.Outline(graphics, Theme.Muted, ClientRectangle, 1.2f * scale);

            int inset = (int)(10 * scale), arrow = (int)(28 * scale);
            var textBounds = new Rectangle(inset, 0, Math.Max(0, Width - inset - arrow), Height);
            string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
            TextRenderer.DrawText(graphics, text, Font, textBounds, Enabled ? Theme.Text : Theme.Faint, fill,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float centerX = Width - 15 * scale, centerY = Height / 2f;
            using (var brush = new SolidBrush(Enabled ? Theme.Muted : Theme.Faint))
                graphics.FillPolygon(brush, new[]
                {
                    new PointF(centerX - 4.5f * scale, centerY - 2.5f * scale),
                    new PointF(centerX + 4.5f * scale, centerY - 2.5f * scale),
                    new PointF(centerX, centerY + 3 * scale)
                });
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
            ForeColor = Theme.Text, UseMnemonic = false, Font = Theme.Strong(10.5f)
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
            down.Font = up.Font = Theme.Strong(12);
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

    // A quiet name followed by its value, e.g. "START 4.05%".
    private sealed class Readout : Control
    {
        private readonly string name;
        private string value = "—";

        public Readout(string name)
        {
            this.name = name;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.StaticText;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Font = Theme.Strong(11);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public string Value
        {
            get { return value; }
            set { if (this.value != value) { this.value = value; AccessibleDescription = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            float scale = Theme.Scale(graphics);
            var back = Theme.ParentColor(this);
            graphics.Clear(back);
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            using (var label = Theme.Label(9))
            {
                // The label and the larger value share a baseline, centred vertically.
                float valueAscent = Ascent(Font, graphics), labelAscent = Ascent(label, graphics);
                float baseline = (Height + valueAscent) / 2f - 1 * scale;
                string caption = name.ToUpperInvariant();
                var labelSize = TextRenderer.MeasureText(graphics, caption, label, Size.Empty, flags);
                TextRenderer.DrawText(graphics, caption, label, new Point(0, (int)(baseline - labelAscent)), Theme.Muted, back, flags);
                int x = labelSize.Width + (int)(7 * scale);
                TextRenderer.DrawText(graphics, value, Font,
                    new Rectangle(x, (int)(baseline - valueAscent), Math.Max(0, Width - x), Font.Height),
                    Enabled ? Theme.Text : Theme.Faint, back, flags | TextFormatFlags.EndEllipsis);
            }
        }

        private static float Ascent(Font font, Graphics graphics)
        {
            var family = font.FontFamily;
            return font.SizeInPoints * graphics.DpiY / 72f * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
        }
    }

    // Recent runs, timing-screen style. Hovering a row shows a remove button; a best that
    // dropped out of the recent list gets its own row at the bottom.
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
            Font = Theme.Type(10.5f);
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
                lines[index] = string.Format("{0}: {1:0.000} seconds, {2}", index == 0 ? "Last run" : (index + 1) + " runs ago",
                    recent[index].Seconds,
                    IsPersonalBest(recent[index]) ? "personal best" : DeltaText(recent[index]) + " to personal best");
            AccessibleDescription = string.Join("; ", lines);
            Invalidate();
        }

        private int RowHeight { get { return (int)(36 * paintScale); } }
        private int HeaderHeight { get { return (int)(28 * paintScale); } }

        private RectangleF RemoveBox(int row)
        {
            float size = 24 * paintScale;
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
            int rowHeight = RowHeight, headerHeight = HeaderHeight;
            int inset = (int)(10 * scale);
            float[] widths = { 0.26f, 0.26f, 0.24f, 0.24f };
            int tableWidth = Math.Max(0, Width - (int)(RemoveBox(0).Width + 12 * scale));
            var columns = new int[widths.Length + 1];
            columns[0] = inset;
            for (int index = 0; index < widths.Length; index++)
                columns[index + 1] = columns[index] + (int)((tableWidth - inset) * widths[index]);
            columns[widths.Length] = tableWidth;

            using (var label = Theme.Label(9))
            using (var strong = Theme.Strong(11))
            using (var line = new Pen(Theme.Line))
            {
                string[] headings = { "TIME", "DELTA", "ENTRY KM/H", "EXIT KM/H" };
                for (int index = 0; index < headings.Length; index++)
                    Cell(graphics, headings[index], label, Theme.Muted, columns, index, 0, headerHeight);
                graphics.DrawLine(line, 0, headerHeight - 1, Width, headerHeight - 1);

                for (int row = 0; row < rows.Length; row++)
                {
                    var run = rows[row];
                    int top = headerHeight + row * rowHeight;
                    bool personalBest = IsPersonalBest(run);
                    if (row == hoverRow || row == 0)
                        Theme.Fill(graphics, row == hoverRow ? Theme.RowHover : Theme.Surface,
                            new RectangleF(0, top, Width, rowHeight - 1));
                    // A best that dropped out of the recent runs sits below a heavier rule.
                    if (row == runs.Length && row > 0)
                        Theme.Fill(graphics, Theme.Muted, new RectangleF(0, top - 1, Width, 2 * scale));
                    Cell(graphics, run.Seconds.ToString("0.000"), strong, personalBest ? Theme.Green : Theme.Text, columns, 0, top, rowHeight);
                    if (personalBest)
                        Cell(graphics, "PB", strong, Theme.Green, columns, 1, top, rowHeight);
                    else if (best != null)
                        Cell(graphics, DeltaText(run), strong, Theme.Yellow, columns, 1, top, rowHeight);
                    Cell(graphics, run.EntrySpeed.ToString("0"), Font, Theme.Muted, columns, 2, top, rowHeight);
                    Cell(graphics, run.ExitSpeed.ToString("0"), Font, Theme.Muted, columns, 3, top, rowHeight);
                    graphics.DrawLine(line, 0, top + rowHeight - 1, Width, top + rowHeight - 1);
                    if (row == hoverRow) PaintRemove(graphics, RemoveBox(row), scale);
                }
            }
        }

        private void PaintRemove(Graphics graphics, RectangleF box, float scale)
        {
            Theme.Fill(graphics, hoverRemove ? Theme.Red : Theme.Inset, box);
            float inset = box.Width * 0.32f;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Text, 1.6f * scale))
            {
                graphics.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                graphics.DrawLine(pen, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
            }
            graphics.SmoothingMode = SmoothingMode.None;
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
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            using (var keyFont = Theme.Strong(8.5f))
            {
                float x = 0;
                int capHeight = (int)(22 * scale), padding = (int)(7 * scale);
                int top = (Height - capHeight) / 2;
                foreach (var hint in hints)
                {
                    var keySize = TextRenderer.MeasureText(graphics, hint[0], keyFont, Size.Empty, flags);
                    var cap = new RectangleF(x, top, keySize.Width + padding * 2, capHeight);
                    Theme.Fill(graphics, Theme.Inset, cap);
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
