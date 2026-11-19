using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly DesktopSession session;
        private readonly Timer refreshTimer = new Timer { Interval = 50 };
        private readonly DarkPicker checkpoint = new DarkPicker("Active save");
        private readonly DarkPicker overlayPosition = new DarkPicker("In-game overlay position");
        private readonly PillButton pinButton = new PillButton("Pin a save…");
        private readonly PillButton loopButton = new PillButton("Arm loop");
        private readonly PillButton bindButton = new PillButton("Bind wheel");
        private readonly PillButton clearWheel = new PillButton("Clear");
        private readonly PillButton mapMarkers = new PillButton("Set on map…");
        private readonly Stepper delay = new Stepper("Countdown", 1, 30, 3);
        private readonly ConnectionBadge connection = new ConnectionBadge();
        private readonly Label checkpointDetail = Caption("", 9.5f);
        private readonly Readout start = new Readout("Start");
        private readonly Readout finish = new Readout("Finish");
        private readonly Label status = Caption("Starting…", 10.5f);
        private readonly Label wheel = Caption("Wheel unbound", 9.5f);
        private readonly Label detail = Caption("", 9.5f);
        private readonly FlushLabel time = new FlushLabel(52, false) { Text = "—", BaselineInset = 14 };
        private readonly FlushLabel delta = new FlushLabel(12.5f, true);
        private readonly Readout best = new Readout("Best");
        private readonly Readout entry = new Readout("Entry");
        private readonly Readout exit = new Readout("Exit");
        private readonly Label emptyHistory = Caption("Cross both timing lines to record a run.", 10);
        private readonly AttemptList history = new AttemptList { Dock = DockStyle.Fill, Visible = false };
        private TableLayoutPanel wheelRow;
        private GameOverlay overlay;
        private DeskSnapshot displayed;
        private string[] pinNames = new string[0];
        private Attempt[] displayedAttempts = new Attempt[0];
        private Attempt displayedBest;

        public PracticeDesk(DesktopSession session)
        {
            this.session = session;
            SuspendLayout();
            Text = "ACC Repeat";
            Font = Theme.Type(10.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            ClientSize = new Size(1000, 698);
            MinimumSize = new Size(840, 738);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.UseDarkTitleBar(this);

            var root = Stack(1);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.Controls.Add(BuildHeader(), 0, 0);
            var workspace = Stack(2);
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 292));
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
            overlayPosition.SelectionChangeCommitted += SelectOverlay;
            pinButton.Click += OpenPinDialog;
            loopButton.Click += ToggleLoop;
            bindButton.Click += BindWheel;
            clearWheel.Click += (sender, args) => session.Post(() => ConfigureWheel("clear"));
            mapMarkers.Click += (sender, args) =>
            {
                using (var dialog = new MarkerMapDialog(session, displayed, Font)) dialog.ShowDialog(this);
            };
            history.RemoveRequested += ConfirmRemove;
            refreshTimer.Tick += (sender, args) =>
            {
                var view = session.Snapshot;
                if (overlay != null) overlay.Update(view);
                Render(view);
            };
            SetActions(false);
            Shown += (sender, args) =>
            {
                overlay = new GameOverlay();
                session.Start();
                refreshTimer.Start();
            };
            FormClosed += (sender, args) =>
            {
                refreshTimer.Stop();
                if (overlay != null) overlay.Dispose();
                overlay = null;
                session.Stop();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                if (overlay != null) overlay.Dispose();
            }
            base.Dispose(disposing);
        }

        private Control BuildHeader()
        {
            var header = Stack(2);
            header.BackColor = Theme.Background;
            header.Padding = new Padding(20, 6, 20, 6);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            var title = new FlushLabel(14, true) { Text = "ACC Repeat" };
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(connection, 1, 0);
            header.Paint += (sender, args) =>
            {
                using (var line = new Pen(Theme.Line)) args.Graphics.DrawLine(line, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            return header;
        }

        private Control BuildSidebar()
        {
            var sidebar = Stack(1);
            sidebar.Padding = new Padding(20, 8, 14, 8);
            sidebar.BackColor = Theme.Surface;
            AddRow(sidebar, SectionHeading("Practice save", false), 28);
            checkpoint.Dock = DockStyle.Fill;
            AddRow(sidebar, checkpoint, 36);
            checkpointDetail.ForeColor = Theme.Muted;
            AddRow(sidebar, checkpointDetail, 26);
            AddRow(sidebar, pinButton, 36);

            AddRow(sidebar, SectionHeading("Timing lines", true), 42);
            AddRow(sidebar, TwoColumns(start, finish), 30);
            AddRow(sidebar, mapMarkers, 36);

            AddRow(sidebar, SectionHeading("Loop", true), 42);
            AddRow(sidebar, status, 28);
            var countdown = Stack(2);
            countdown.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            var countdownLabel = Caption("Countdown", 10);
            countdownLabel.ForeColor = Theme.Muted;
            countdown.Controls.Add(countdownLabel, 0, 0);
            delay.Margin = new Padding(0, 0, 6, 0);
            countdown.Controls.Add(delay, 1, 0);
            AddRow(sidebar, countdown, 34);
            loopButton.Primary = true;
            AddRow(sidebar, loopButton, 38);

            AddRow(sidebar, SectionHeading("Wheel reset", true), 42);
            wheel.ForeColor = Theme.Muted;
            AddRow(sidebar, wheel, 24);
            wheelRow = TwoColumns(bindButton, clearWheel);
            AddRow(sidebar, wheelRow, 36);

            AddRow(sidebar, SectionHeading("In-game overlay", true), 42);
            overlayPosition.Dock = DockStyle.Fill;
            overlayPosition.Items.AddRange(OverlayPositions.Cast<object>().ToArray());
            overlayPosition.SelectedIndex = 1;
            AddRow(sidebar, overlayPosition, 36);
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.Controls.Add(new Panel { Margin = Padding.Empty }, 0, sidebar.RowStyles.Count - 1);
            return sidebar;
        }

        // The newest run's time leads; its delta and speeds sit under it, then the run history.
        private Control BuildResults()
        {
            var results = Stack(1);
            results.Padding = new Padding(28, 10, 24, 8);
            time.Font = Theme.Type(52, FontStyle.Bold);
            AddRow(results, time, 92);
            AddRow(results, delta, 28);
            var readouts = Stack(3);
            readouts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            readouts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            readouts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            readouts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            readouts.Controls.Add(best, 0, 0);
            readouts.Controls.Add(entry, 1, 0);
            readouts.Controls.Add(exit, 2, 0);
            AddRow(results, readouts, 34);
            AddRow(results, new Panel { Margin = Padding.Empty }, 24);
            var historySurface = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            historySurface.Controls.Add(history);
            emptyHistory.ForeColor = Theme.Muted;
            emptyHistory.TextAlign = ContentAlignment.TopLeft;
            emptyHistory.Padding = new Padding(0, 4, 0, 0);
            historySurface.Controls.Add(emptyHistory);
            results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            results.Controls.Add(historySurface, 0, 4);
            return results;
        }

        private Control BuildFooter()
        {
            var footer = Stack(1);
            footer.BackColor = Theme.Background;
            footer.Padding = new Padding(20, 6, 20, 0);
            footer.Paint += (sender, args) =>
            {
                using (var line = new Pen(Theme.Line)) args.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
            };
            var keys = new KeyHints(
                new[] { "Ctrl+Shift+J", "Reset" }, new[] { "Ctrl+Shift+K", "Mark start" },
                new[] { "Ctrl+Shift+L", "Mark finish" }, new[] { "F9", "Disarm loop" })
            {
                Dock = DockStyle.Fill, Font = Theme.Type(9.5f)
            };
            AddRow(footer, keys, 28);
            detail.ForeColor = Theme.Muted;
            AddRow(footer, detail, 28);
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

        private void SelectOverlay(object sender, EventArgs args)
        {
            if (overlayPosition.SelectedIndex < 0) return;
            string position = OverlayPositions[overlayPosition.SelectedIndex];
            session.Post(() => SetOverlayPosition(position));
        }

        private void OpenPinDialog(object sender, EventArgs args)
        {
            var available = displayed.AvailableSaves;
            using (var dialog = new Form
            {
                Text = "Pin or rename a Free Practice save", BackColor = Theme.Surface, ForeColor = Theme.Text,
                Font = Font, ClientSize = new Size(495, 205), FormBorderStyle = FormBorderStyle.FixedSingle, Icon = Icon,
                StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false
            })
            {
                Theme.UseDarkTitleBar(dialog);
                dialog.SuspendLayout();
                var body = Stack(1);
                body.Padding = new Padding(18, 13, 12, 10);
                var saves = new DarkPicker("Free Practice save") { Dock = DockStyle.Fill };
                saves.Items.AddRange(available.Select(save => (object)((save.Row + 1) + ". " + save.Label)).ToArray());
                if (available.Length != 0) saves.SelectedIndex = 0;
                var name = new TextBox
                {
                    Dock = DockStyle.Fill, AccessibleName = "Pin name", MaxLength = 64,
                    BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.None
                };
                var nameField = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Inset, Margin = new Padding(0, 2, 6, 2), Padding = new Padding(10, 9, 10, 0) };
                nameField.Controls.Add(name);
                nameField.Click += (source, click) => name.Focus();
                nameField.Paint += (source, paint) =>
                {
                    if (name.Focused)
                        Theme.Outline(paint.Graphics, Theme.Muted, nameField.ClientRectangle, 1.2f * Theme.Scale(paint.Graphics));
                };
                name.GotFocus += (source, focus) => nameField.Invalidate();
                name.LostFocus += (source, focus) => nameField.Invalidate();
                var accept = new PillButton("Save pin") { Primary = true, Enabled = false };
                accept.DialogResult = DialogResult.OK;
                var cancel = new PillButton("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                name.TextChanged += (source, change) => accept.Enabled = name.Text.Trim().Length != 0 && saves.SelectedIndex >= 0;
                AddRow(body, SectionHeading("Free Practice save", false), 25);
                AddRow(body, saves, 38);
                AddRow(body, SectionHeading("Pin name", false), 30);
                AddRow(body, nameField, 38);
                AddRow(body, TwoColumns(cancel, accept), 40);
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

        private void ConfirmRemove(Attempt run)
        {
            bool isBest = displayed != null && displayed.Best != null && displayed.Best.Seconds == run.Seconds;
            string question = string.Format("Remove the {0:0.000} s run from your history?{1}\n\nThis can't be undone.",
                run.Seconds, isBest ? " It's your personal best; the quickest run left in the list becomes your best." : "");
            if (MessageBox.Show(this, question, "Remove run", MessageBoxButtons.OKCancel, MessageBoxIcon.None,
                MessageBoxDefaultButton.Button2) == DialogResult.OK)
                session.Post(() => RemoveRun(run));
        }

        private void ToggleLoop(object sender, EventArgs args)
        {
            string command = displayed.LoopArmed ? "off" : "on " + delay.Value;
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
                status.Text = "ACC Repeat could not start";
                status.ForeColor = Theme.Yellow;
                detail.Text = view.Error;
                return;
            }

            connection.Connected = view.Connected;
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
            int overlayIndex = Array.IndexOf(OverlayPositions, view.OverlayPosition);
            if (overlayIndex >= 0 && !overlayPosition.DroppedDown && overlayPosition.SelectedIndex != overlayIndex)
                overlayPosition.SelectedIndex = overlayIndex;
            checkpointDetail.Text = view.CheckpointDetail;
            start.Value = FormatMarker(view.Start);
            finish.Value = FormatMarker(view.Finish);
            status.Text = view.Status;
            status.ForeColor = Theme.Text;
            detail.Text = string.IsNullOrEmpty(view.ShortcutWarning) ? view.Detail : view.ShortcutWarning;
            detail.ForeColor = string.IsNullOrEmpty(view.ShortcutWarning) ? Theme.Muted : Theme.Yellow;
            wheel.Text = view.LearningWheel ? "Press a wheel button now…" : view.Wheel == null ? "No wheel button bound" :
                "Button " + (view.Wheel.Button + 1) + " on " + view.Wheel.Device;
            bindButton.Text = view.LearningWheel ? "Cancel" : view.Wheel == null ? "Bind wheel" : "Rebind";
            loopButton.Text = view.LoopArmed ? "Disarm loop" : "Arm loop";
            SetActions(!view.Reloading);
            RenderResults(view);
        }

        private void SetActions(bool enabled)
        {
            checkpoint.Enabled = bindButton.Enabled = overlayPosition.Enabled = enabled;
            mapMarkers.Enabled = enabled && displayed.Track != null;
            pinButton.Enabled = enabled && displayed.AvailableSaves.Length != 0;
            bool wheelBound = displayed != null && displayed.Wheel != null && !displayed.LearningWheel;
            clearWheel.Enabled = enabled && wheelBound;
            if (clearWheel.Visible != wheelBound)
            {
                clearWheel.Visible = wheelBound;
                wheelRow.ColumnStyles[1] = wheelBound ? new ColumnStyle(SizeType.Percent, 50) : new ColumnStyle(SizeType.Absolute, 0);
            }
            loopButton.Enabled = enabled && (displayed.CanArm || displayed.LoopArmed);
            loopButton.Primary = displayed == null || !displayed.LoopArmed;
            delay.Enabled = enabled && !displayed.LoopArmed;
        }

        private void RenderResults(DeskSnapshot view)
        {
            var last = view.Recent.FirstOrDefault();
            time.Text = last == null ? "—" : last.Seconds.ToString("0.000");
            time.ForeColor = last == null ? Theme.Faint : IsBest(last, view.Best) ? Theme.Green : Theme.Text;
            entry.Value = last == null ? "—" : FormatSpeed(last.EntrySpeed);
            exit.Value = last == null ? "—" : FormatSpeed(last.ExitSpeed);
            best.Value = view.Best == null ? "—" : view.Best.Seconds.ToString("0.000");
            best.ValueColor = view.Best == null ? Theme.Text : Theme.Green;

            delta.Text = "";
            if (last != null && last.PreviousBestDelta.HasValue && last.PreviousBestDelta.Value < 0)
            {
                delta.Text = string.Format("New personal best, {0:0.000} faster", -last.PreviousBestDelta.Value);
                delta.ForeColor = Theme.Green;
            }
            else if (last != null && last.PreviousBestDelta.HasValue)
            {
                delta.Text = string.Format("{0:+0.000;-0.000;0.000} to the previous best", last.PreviousBestDelta.Value);
                delta.ForeColor = Theme.Yellow;
            }
            else if (last != null && view.Best != null && !IsBest(last, view.Best))
            {
                delta.Text = string.Format("{0:+0.000;-0.000;0.000} to your best", last.Seconds - view.Best.Seconds);
                delta.ForeColor = Theme.Yellow;
            }
            else if (IsBest(last, view.Best))
            {
                delta.Text = "Personal best";
                delta.ForeColor = Theme.Green;
            }

            if (!displayedAttempts.SequenceEqual(view.Recent) || displayedBest != view.Best)
            {
                history.SetRuns(view.Recent, view.Best);
                displayedAttempts = view.Recent;
                displayedBest = view.Best;
            }
            bool hasAttempts = view.Recent.Length != 0;
            history.Visible = hasAttempts;
            emptyHistory.Visible = !hasAttempts;
            emptyHistory.Text = view.Finish.HasValue ? "Cross both timing lines to record a run." :
                view.Start.HasValue ? "In ACC, press Ctrl+Shift+L after the exit to mark the finish." :
                "Select Set on map to click the timing lines onto the track, or press Ctrl+Shift+K and Ctrl+Shift+L in ACC.";
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

        private static Label Caption(string text, float size)
        {
            return new Label
            {
                Text = text, Font = Theme.Type(size), Dock = DockStyle.Fill,
                ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
                AutoEllipsis = true, UseMnemonic = false
            };
        }

        // A condensed capital group title; groups after the first get a rule above them.
        private static Label SectionHeading(string text, bool rule)
        {
            var label = Caption(text.ToUpperInvariant(), 10);
            label.Font = Theme.Label(10);
            label.ForeColor = Theme.Muted;
            label.TextAlign = ContentAlignment.BottomLeft;
            label.Padding = new Padding(0, 0, 0, 5);
            label.Margin = new Padding(0, 0, 6, 0);
            if (rule)
                label.Paint += (sender, args) =>
                {
                    using (var line = new Pen(Theme.Line)) args.Graphics.DrawLine(line, 0, 6, label.Width, 6);
                };
            return label;
        }
    }
}
