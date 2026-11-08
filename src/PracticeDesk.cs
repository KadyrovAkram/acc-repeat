using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal static class PracticeDeskApp
{
    [STAThread]
    private static void Main() { RepeatCorner.OpenDesktop(); }
}

internal static partial class RepeatCorner
{
    internal static void OpenDesktop()
    {
        EnableDpiAwareness();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var session = new DesktopSession();
        using (var window = new PracticeDesk(session))
            Application.Run(window);
        session.Join();
    }

    private static string FormatSpeed(double speed) { return speed.ToString("0") + " km/h"; }

    private sealed class PracticeDesk : Form
    {
        private static readonly Color Background = Color.FromArgb(28, 29, 33);
        private static readonly Color Surface = Color.FromArgb(35, 37, 42);
        private static readonly Color Inset = Color.FromArgb(44, 46, 53);
        private static readonly Color TextColor = Color.FromArgb(244, 244, 248);
        private static readonly Color Muted = Color.FromArgb(184, 186, 196);
        private static readonly Color Red = Color.FromArgb(172, 32, 53);
        private static readonly Color Green = Color.FromArgb(125, 225, 174);

        private readonly DesktopSession session;
        private readonly Timer refreshTimer = new Timer { Interval = 100 };
        private readonly ComboBox checkpoint = Picker("Active checkpoint");
        private readonly Button pinButton = ActionButton("Pin a save…");
        private readonly Button loopButton = ActionButton("Arm loop");
        private readonly Button bindButton = ActionButton("Bind wheel");
        private readonly Button clearWheel = ActionButton("Clear");
        private readonly NumericUpDown delay = new NumericUpDown
        {
            Minimum = 1, Maximum = 30, Value = 3, Width = 54, AccessibleName = "Countdown seconds",
            BackColor = Inset, ForeColor = TextColor
        };
        private readonly Label connection = Caption("Waiting for ACC", 10);
        private readonly Label checkpointDetail = Caption("", 9);
        private readonly Label start = Caption("Start   —", 10);
        private readonly Label finish = Caption("Finish   —", 10);
        private readonly Label status = Caption("Starting…", 10);
        private readonly Label wheel = Caption("Wheel unbound", 9);
        private readonly Label detail = Caption("", 9);
        private readonly Label time = Caption("—", 37);
        private readonly Label best = Caption("Personal best   —", 11);
        private readonly Label entry = Caption("—", 17);
        private readonly Label exit = Caption("—", 17);
        private readonly Label emptyHistory = Caption("Cross both timing markers to record a run.", 10);
        private readonly DataGridView history = CreateHistory();
        private DeskSnapshot displayed;
        private string[] pinNames = new string[0];
        private Attempt[] displayedAttempts = new Attempt[0];
        private Attempt displayedBest;

        public PracticeDesk(DesktopSession session)
        {
            this.session = session;
            SuspendLayout();
            Text = "ACC Repeat Corner";
            Font = new Font("Segoe UI", 10);
            BackColor = Background;
            ForeColor = TextColor;
            ClientSize = new Size(1000, 620);
            MinimumSize = new Size(820, 625);
            StartPosition = FormStartPosition.CenterScreen;

            var root = Stack(1);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            root.Controls.Add(BuildHeader(), 0, 0);
            var workspace = Stack(2);
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.Controls.Add(BuildSidebar(), 0, 0);
            workspace.Controls.Add(BuildResults(), 1, 0);
            root.Controls.Add(workspace, 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);

            checkpoint.SelectionChangeCommitted += SelectCheckpoint;
            pinButton.Click += OpenPinDialog;
            loopButton.Click += ToggleLoop;
            bindButton.Click += BindWheel;
            clearWheel.Click += (sender, args) => session.Post(() => ConfigureWheel("clear"));
            refreshTimer.Tick += (sender, args) => Render(session.Snapshot);
            SetActions(false);
            Shown += (sender, args) =>
            {
                using (var graphics = CreateGraphics())
                {
                    int rowHeight = (int)Math.Round(27 * graphics.DpiY / 96);
                    history.RowTemplate.Height = rowHeight;
                    history.ColumnHeadersHeight = rowHeight;
                }
                session.Start();
                refreshTimer.Start();
            };
            FormClosed += (sender, args) => session.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refreshTimer.Dispose();
            base.Dispose(disposing);
        }

        private Control BuildHeader()
        {
            var header = Stack(3);
            header.BackColor = Surface;
            header.Padding = new Padding(18, 9, 18, 9);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            var mark = Caption("RC", 11);
            mark.BackColor = Red;
            mark.Font = new Font("Segoe UI", 11, FontStyle.Bold | FontStyle.Italic);
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.Margin = new Padding(0, 4, 7, 4);
            var title = Caption("Practice desk", 15);
            title.Margin = new Padding(8, 0, 0, 0);
            connection.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(mark, 0, 0);
            header.Controls.Add(title, 1, 0);
            header.Controls.Add(connection, 2, 0);
            return header;
        }

        private Control BuildSidebar()
        {
            var sidebar = Stack(1);
            sidebar.Padding = new Padding(18, 13, 18, 8);
            sidebar.BackColor = Surface;
            AddRow(sidebar, SectionHeading("Active checkpoint"), 21);
            checkpoint.Dock = DockStyle.Fill;
            AddRow(sidebar, checkpoint, 31);
            checkpointDetail.ForeColor = Muted;
            checkpointDetail.TextAlign = ContentAlignment.TopLeft;
            AddRow(sidebar, checkpointDetail, 42);
            AddRow(sidebar, pinButton, 31);
            AddRow(sidebar, SectionHeading("Timing section"), 33);
            var markers = TwoColumns(start, finish);
            markers.BackColor = Inset;
            markers.Padding = new Padding(9, 0, 5, 0);
            AddRow(sidebar, markers, 34);

            AddRow(sidebar, SectionHeading("Hands-free loop"), 34);
            AddRow(sidebar, status, 42);
            var countdown = Stack(2);
            countdown.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            countdown.Controls.Add(Caption("Countdown (seconds)", 9), 0, 0);
            delay.Anchor = AnchorStyles.Left;
            countdown.Controls.Add(delay, 1, 0);
            AddRow(sidebar, countdown, 30);
            loopButton.BackColor = Red;
            AddRow(sidebar, loopButton, 32);

            AddRow(sidebar, SectionHeading("Wheel retry"), 33);
            AddRow(sidebar, wheel, 25);
            AddRow(sidebar, TwoColumns(bindButton, clearWheel), 32);
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.Controls.Add(new Panel { Margin = Padding.Empty }, 0, sidebar.RowStyles.Count - 1);
            return sidebar;
        }

        private Control BuildResults()
        {
            var results = Stack(1);
            results.Padding = new Padding(24, 13, 24, 8);
            AddRow(results, SectionHeading("Last attempt"), 22);
            time.Font = new Font("Segoe UI", 37, FontStyle.Bold);
            AddRow(results, time, 66);
            AddRow(results, best, 30);
            AddRow(results, TwoColumns(SpeedCard("Entry speed", entry), SpeedCard("Exit speed", exit)), 61);
            AddRow(results, SectionHeading("Recent attempts"), 36);
            var historySurface = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            historySurface.Controls.Add(history);
            emptyHistory.ForeColor = Muted;
            emptyHistory.TextAlign = ContentAlignment.TopLeft;
            emptyHistory.Padding = new Padding(0, 12, 0, 0);
            historySurface.Controls.Add(emptyHistory);
            results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            results.Controls.Add(historySurface, 0, 5);
            return results;
        }

        private Control BuildFooter()
        {
            var footer = Stack(1);
            footer.BackColor = Surface;
            footer.Padding = new Padding(18, 0, 18, 0);
            var keys = Caption("Ctrl+Shift+J  Retry     ·     Ctrl+Shift+K  Mark start     ·     Ctrl+Shift+L  Mark finish     ·     F9  Stop loop", 9);
            AddRow(footer, keys, 25);
            detail.ForeColor = Muted;
            detail.TextAlign = ContentAlignment.TopLeft;
            AddRow(footer, detail, 32);
            return footer;
        }

        private void SelectCheckpoint(object sender, EventArgs args)
        {
            int index = checkpoint.SelectedIndex;
            if (index == 0) session.Post(UseLatest);
            else if (index > 0)
            {
                string name = pinNames[index - 1];
                session.Post(() => UseCheckpoint(name));
            }
        }

        private void OpenPinDialog(object sender, EventArgs args)
        {
            var available = displayed.AvailableSaves;
            using (var dialog = new Form
            {
                Text = "Pin a Free Practice save", BackColor = Surface, ForeColor = TextColor,
                Font = Font, ClientSize = new Size(495, 193), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false
            })
            {
                dialog.SuspendLayout();
                var body = Stack(1);
                body.Padding = new Padding(18, 13, 18, 10);
                var saves = Picker("Free Practice save");
                saves.Dock = DockStyle.Fill;
                saves.Items.AddRange(available.Select(save => (object)((save.Row + 1) + ". " + save.Label)).ToArray());
                if (available.Length != 0) saves.SelectedIndex = 0;
                var name = new TextBox
                {
                    Dock = DockStyle.Fill, AccessibleName = "Checkpoint name", MaxLength = 64,
                    BackColor = Inset, ForeColor = TextColor
                };
                var accept = ActionButton("Pin checkpoint");
                accept.DialogResult = DialogResult.OK;
                accept.BackColor = Red;
                accept.Enabled = false;
                var cancel = ActionButton("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                name.TextChanged += (source, change) => accept.Enabled = name.Text.Trim().Length != 0 && saves.SelectedIndex >= 0;
                AddRow(body, SectionHeading("Free Practice save"), 25);
                AddRow(body, saves, 36);
                AddRow(body, SectionHeading("Checkpoint name"), 27);
                AddRow(body, name, 40);
                AddRow(body, TwoColumns(cancel, accept), 36);
                dialog.Controls.Add(body);
                dialog.AcceptButton = accept;
                dialog.CancelButton = cancel;
                dialog.AutoScaleDimensions = new SizeF(96, 96);
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.ResumeLayout(true);
                if (dialog.ShowDialog(this) == DialogResult.OK && saves.SelectedIndex >= 0)
                {
                    var selected = available[saves.SelectedIndex];
                    string newName = name.Text;
                    session.Post(() => PinCheckpoint(ReadSaves(), selected, newName));
                }
            }
        }

        private void ToggleLoop(object sender, EventArgs args)
        {
            string command = displayed.LoopArmed ? "off" : "on " + (int)delay.Value;
            session.Post(() => ConfigureLoop(command));
        }

        private void BindWheel(object sender, EventArgs args)
        {
            if (displayed.LearningWheel)
                session.Post(() => { learnWheel = null; ReportStatus("Wheel binding cancelled."); });
            else
                session.Post(() => ConfigureWheel("bind"));
        }

        private void Render(DeskSnapshot view)
        {
            if (view == null || view == displayed) return;
            displayed = view;
            if (view.Error != null)
            {
                SetActions(false);
                status.Text = "Repeat Corner could not start";
                detail.Text = view.Error;
                return;
            }

            connection.Text = view.Connected ? "●  ACC connected" : "○  Waiting for ACC";
            connection.ForeColor = view.Connected ? Green : Muted;
            if (checkpoint.Items.Count == 0 || !pinNames.SequenceEqual(view.PinNames))
            {
                pinNames = view.PinNames;
                checkpoint.BeginUpdate();
                checkpoint.Items.Clear();
                checkpoint.Items.Add("Latest save");
                checkpoint.Items.AddRange(pinNames);
                checkpoint.EndUpdate();
            }
            int selectedIndex = Array.IndexOf(pinNames, view.ActivePin) + 1;
            if (!checkpoint.DroppedDown && checkpoint.SelectedIndex != selectedIndex)
                checkpoint.SelectedIndex = selectedIndex;
            checkpointDetail.Text = view.CheckpointDetail;
            start.Text = "Start   " + FormatMarker(view.Start);
            finish.Text = "Finish   " + FormatMarker(view.Finish);
            status.Text = view.Status;
            status.ForeColor = view.LoopArmed && !view.Reloading ? Green : TextColor;
            detail.Text = string.IsNullOrEmpty(view.ShortcutWarning) ? view.Detail : view.ShortcutWarning;
            wheel.Text = view.LearningWheel ? "Waiting for a button press…" : view.Wheel == null ? "Wheel unbound" :
                "Button " + (view.Wheel.Button + 1) + " · " + view.Wheel.Device;
            bindButton.Text = view.LearningWheel ? "Cancel" : "Bind wheel";
            loopButton.Text = view.LoopArmed ? "Disarm loop" : "Arm loop";
            SetActions(!view.Reloading);
            RenderResults(view);
        }

        private void SetActions(bool enabled)
        {
            checkpoint.Enabled = bindButton.Enabled = enabled;
            pinButton.Enabled = enabled && displayed.AvailableSaves.Length != 0;
            clearWheel.Enabled = enabled && displayed.Wheel != null;
            loopButton.Enabled = enabled && (displayed.CanArm || displayed.LoopArmed);
            loopButton.BackColor = loopButton.Enabled && !displayed.LoopArmed ? Red : Inset;
            delay.Enabled = enabled && !displayed.LoopArmed;
        }

        private void RenderResults(DeskSnapshot view)
        {
            var last = view.Recent.FirstOrDefault();
            time.Text = last == null ? "—" : last.Seconds.ToString("0.000") + " s";
            entry.Text = last == null ? "—" : FormatSpeed(last.EntrySpeed);
            exit.Text = last == null ? "—" : FormatSpeed(last.ExitSpeed);
            string personalBest = view.Best == null ? "Personal best   —" : string.Format("Personal best   {0:0.000} s", view.Best.Seconds);
            best.Text = personalBest;
            if (last != null && last.PreviousBestDelta.HasValue)
                best.Text = string.Format("{0:+0.000;-0.000;0.000} s vs previous best   ·   {1}", last.PreviousBestDelta.Value, personalBest);
            else if (last != null && view.Best != null && !IsBest(last, view.Best))
                best.Text = string.Format("{0:+0.000;-0.000;0.000} s off best   ·   {1}", last.Seconds - view.Best.Seconds, personalBest);
            best.ForeColor = IsBest(last, view.Best) ? Green : Muted;

            if (!displayedAttempts.SequenceEqual(view.Recent) || displayedBest != view.Best)
            {
                history.Rows.Clear();
                for (int index = 0; index < view.Recent.Length; index++)
                {
                    var run = view.Recent[index];
                    string label = index == 0 ? "Latest" : (index + 1).ToString();
                    bool personalRecord = IsBest(run, view.Best);
                    int row = history.Rows.Add(label + (personalRecord ? " · PB" : ""),
                        run.Seconds.ToString("0.000") + " s", FormatSpeed(run.EntrySpeed), FormatSpeed(run.ExitSpeed));
                    if (personalRecord) history.Rows[row].DefaultCellStyle.ForeColor = Green;
                }
                history.ClearSelection();
                displayedAttempts = view.Recent;
                displayedBest = view.Best;
            }
            bool hasAttempts = view.Recent.Length != 0;
            history.Visible = hasAttempts;
            emptyHistory.Visible = !hasAttempts;
            emptyHistory.Text = view.Finish.HasValue ? "Cross both timing markers to record a run." :
                "In ACC, mark the start with Ctrl+Shift+K and the finish with Ctrl+Shift+L.";
        }

        private static bool IsBest(Attempt run, Attempt best)
        {
            return run != null && best != null && run.Seconds == best.Seconds;
        }

        private static string FormatMarker(double? marker)
        {
            return marker.HasValue ? marker.Value.ToString("0.00%") : "—";
        }

        private static TableLayoutPanel Stack(int columns)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, Margin = Padding.Empty };
        }

        private static void AddRow(TableLayoutPanel table, Control control, int height)
        {
            int row = table.RowStyles.Count;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            table.Controls.Add(control, 0, row);
        }

        private static TableLayoutPanel TwoColumns(Control left, Control right)
        {
            var row = Stack(2);
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.Controls.Add(left, 0, 0);
            row.Controls.Add(right, 1, 0);
            return row;
        }

        private static Control SpeedCard(string name, Label value)
        {
            var card = Stack(1);
            card.BackColor = Inset;
            card.Margin = new Padding(0, 3, 9, 0);
            card.Padding = new Padding(10, 3, 7, 2);
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            card.Controls.Add(SectionHeading(name), 0, 0);
            card.Controls.Add(value, 0, 1);
            return card;
        }

        private static Label Caption(string text, float size)
        {
            return new Label
            {
                Text = text, Font = new Font("Segoe UI", size), Dock = DockStyle.Fill,
                ForeColor = TextColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
                AutoEllipsis = true, UseMnemonic = false
            };
        }

        private static Label SectionHeading(string text)
        {
            var label = Caption(text.ToUpperInvariant(), 9);
            label.ForeColor = Muted;
            return label;
        }

        private static Button ActionButton(string text)
        {
            var button = new Button
            {
                Text = text, Dock = DockStyle.Fill, BackColor = Inset, ForeColor = TextColor,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 1, 4, 1), UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static ComboBox Picker(string name)
        {
            return new ComboBox
            {
                AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Inset, ForeColor = TextColor, FlatStyle = FlatStyle.Flat
            };
        }

        private static DataGridView CreateHistory()
        {
            var grid = new DataGridView
            {
                AccessibleName = "Recent attempts", Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false, RowHeadersVisible = false, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
                BackgroundColor = Background, GridColor = Inset, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 27
            };
            grid.RowTemplate.Height = 27;
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Background, ForeColor = TextColor, SelectionBackColor = Inset, SelectionForeColor = TextColor
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Muted };
            grid.Columns.Add("run", "Run");
            grid.Columns.Add("time", "Time");
            grid.Columns.Add("entry", "Entry");
            grid.Columns.Add("exit", "Exit");
            foreach (DataGridViewColumn column in grid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            return grid;
        }
    }
}
