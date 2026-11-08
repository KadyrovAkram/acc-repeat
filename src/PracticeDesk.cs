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
        private readonly DesktopSession session;
        private readonly Timer refreshTimer = new Timer { Interval = 50 };
        private readonly DarkPicker checkpoint = new DarkPicker("Active checkpoint");
        private readonly DarkPicker overlayPosition = new DarkPicker("In-game overlay position");
        private readonly PillButton pinButton = new PillButton("Pin a save…");
        private readonly PillButton loopButton = new PillButton("Arm loop");
        private readonly PillButton bindButton = new PillButton("Bind wheel");
        private readonly PillButton clearWheel = new PillButton("Clear");
        private readonly PillButton mapMarkers = new PillButton("Place on map…");
        private readonly Stepper delay = new Stepper("Countdown", 1, 30, 3);
        private readonly Label connection = Caption("Waiting for ACC", 10);
        private readonly Label checkpointDetail = Caption("", 9);
        private readonly Label start = Caption("Start   —", 10);
        private readonly Label finish = Caption("Finish   —", 10);
        private readonly Label status = Caption("Starting…", 10);
        private readonly Label wheel = Caption("Wheel unbound", 9);
        private readonly Label detail = Caption("", 9);
        private readonly Label time = Caption("—", 40);
        private readonly Label best = Caption("Personal best   —", 11);
        private readonly Label entry = Caption("—", 17);
        private readonly Label exit = Caption("—", 17);
        private readonly Label emptyHistory = Caption("Cross both timing markers to record a run.", 10);
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
            Text = "ACC Repeat Corner";
            Font = new Font("Segoe UI", 10);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            ClientSize = new Size(1000, 698);
            MinimumSize = new Size(840, 738);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.UseDarkTitleBar(this);

            var root = Stack(1);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
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
            var header = Stack(3);
            header.BackColor = Theme.Surface;
            header.Padding = new Padding(18, 9, 18, 9);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            var mark = Caption("RC", 11);
            mark.BackColor = Theme.Red;
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
            sidebar.Padding = new Padding(18, 10, 16, 8);
            sidebar.BackColor = Theme.Surface;
            AddRow(sidebar, SectionHeading("Active checkpoint"), 26);
            checkpoint.Dock = DockStyle.Fill;
            AddRow(sidebar, checkpoint, 36);
            checkpointDetail.ForeColor = Theme.Muted;
            AddRow(sidebar, checkpointDetail, 26);
            AddRow(sidebar, pinButton, 36);

            AddRow(sidebar, SectionHeading("Timing section"), 36);
            var markers = TwoColumns(start, finish);
            markers.BackColor = Theme.Inset;
            markers.Padding = new Padding(10, 0, 6, 0);
            markers.Margin = new Padding(0, 0, 6, 0);
            AddRow(sidebar, markers, 34);
            AddRow(sidebar, mapMarkers, 38);

            AddRow(sidebar, SectionHeading("Hands-free loop"), 36);
            AddRow(sidebar, status, 30);
            var countdown = Stack(2);
            countdown.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            var countdownLabel = Caption("Countdown", 9);
            countdownLabel.ForeColor = Theme.Muted;
            countdown.Controls.Add(countdownLabel, 0, 0);
            delay.Margin = new Padding(0, 0, 6, 0);
            countdown.Controls.Add(delay, 1, 0);
            AddRow(sidebar, countdown, 34);
            loopButton.Primary = true;
            AddRow(sidebar, loopButton, 38);

            AddRow(sidebar, SectionHeading("Wheel retry"), 36);
            wheel.ForeColor = Theme.Muted;
            AddRow(sidebar, wheel, 24);
            wheelRow = TwoColumns(bindButton, clearWheel);
            AddRow(sidebar, wheelRow, 36);

            AddRow(sidebar, SectionHeading("In-game overlay"), 36);
            overlayPosition.Dock = DockStyle.Fill;
            overlayPosition.Items.AddRange(OverlayPositions.Cast<object>().ToArray());
            overlayPosition.SelectedIndex = 1;
            AddRow(sidebar, overlayPosition, 36);
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.Controls.Add(new Panel { Margin = Padding.Empty }, 0, sidebar.RowStyles.Count - 1);
            return sidebar;
        }

        private Control BuildResults()
        {
            var results = Stack(1);
            results.Padding = new Padding(26, 14, 26, 8);
            AddRow(results, SectionHeading("Last attempt"), 22);
            time.Font = new Font("Segoe UI", 40, FontStyle.Bold);
            time.Margin = new Padding(0, 0, 0, 0);
            AddRow(results, time, 72);
            AddRow(results, best, 30);
            AddRow(results, TwoColumns(SpeedCard("Entry speed", entry), SpeedCard("Exit speed", exit)), 70);
            AddRow(results, SectionHeading("Recent attempts"), 40);
            var historySurface = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            historySurface.Controls.Add(history);
            emptyHistory.ForeColor = Theme.Muted;
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
            footer.BackColor = Theme.Surface;
            footer.Padding = new Padding(18, 6, 18, 0);
            var keys = new KeyHints(
                new[] { "Ctrl+Shift+J", "Retry" }, new[] { "Ctrl+Shift+K", "Mark start" },
                new[] { "Ctrl+Shift+L", "Mark finish" }, new[] { "F9", "Stop loop" })
            {
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9)
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
                Text = "Pin or rename a Free Practice checkpoint", BackColor = Theme.Surface, ForeColor = Theme.Text,
                Font = Font, ClientSize = new Size(495, 205), FormBorderStyle = FormBorderStyle.FixedDialog,
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
                    Dock = DockStyle.Fill, AccessibleName = "Checkpoint name", MaxLength = 64,
                    BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 4, 6, 4)
                };
                var accept = new PillButton("Save checkpoint") { Primary = true, Enabled = false };
                accept.DialogResult = DialogResult.OK;
                var cancel = new PillButton("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                name.TextChanged += (source, change) => accept.Enabled = name.Text.Trim().Length != 0 && saves.SelectedIndex >= 0;
                AddRow(body, SectionHeading("Free Practice save"), 25);
                AddRow(body, saves, 38);
                AddRow(body, SectionHeading("Checkpoint name"), 30);
                AddRow(body, name, 38);
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
                status.Text = "Repeat Corner could not start";
                status.ForeColor = Theme.Amber;
                detail.Text = view.Error;
                return;
            }

            connection.Text = view.Connected ? "●  ACC connected" : "○  Waiting for ACC";
            connection.ForeColor = view.Connected ? Theme.Green : Theme.Muted;
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
            start.Text = "Start   " + FormatMarker(view.Start);
            finish.Text = "Finish   " + FormatMarker(view.Finish);
            start.ForeColor = view.Start.HasValue ? Theme.Text : Theme.Muted;
            finish.ForeColor = view.Finish.HasValue ? Theme.Text : Theme.Muted;
            status.Text = view.Status;
            status.ForeColor = view.LoopArmed && !view.Reloading ? Theme.Green : Theme.Text;
            detail.Text = string.IsNullOrEmpty(view.ShortcutWarning) ? view.Detail : view.ShortcutWarning;
            detail.ForeColor = string.IsNullOrEmpty(view.ShortcutWarning) ? Theme.Muted : Theme.Amber;
            wheel.Text = view.LearningWheel ? "Press a wheel button now…" : view.Wheel == null ? "No wheel button bound" :
                "Button " + (view.Wheel.Button + 1) + " · " + view.Wheel.Device;
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
            time.Text = last == null ? "—" : last.Seconds.ToString("0.000") + " s";
            time.ForeColor = last == null ? Theme.Faint : IsBest(last, view.Best) ? Theme.Green : Theme.Text;
            entry.Text = last == null ? "—" : FormatSpeed(last.EntrySpeed);
            exit.Text = last == null ? "—" : FormatSpeed(last.ExitSpeed);
            string personalBest = view.Best == null ? "Personal best   —" : string.Format("Personal best   {0:0.000} s", view.Best.Seconds);
            best.Text = personalBest;
            best.ForeColor = Theme.Muted;
            if (last != null && last.PreviousBestDelta.HasValue)
            {
                best.Text = string.Format("{0:+0.000;-0.000;0.000} s vs previous best   ·   {1}", last.PreviousBestDelta.Value, personalBest);
                best.ForeColor = Theme.Green;
            }
            else if (last != null && view.Best != null && !IsBest(last, view.Best))
            {
                best.Text = string.Format("{0:+0.000;-0.000;0.000} s off best   ·   {1}", last.Seconds - view.Best.Seconds, personalBest);
                best.ForeColor = Theme.Amber;
            }
            else if (IsBest(last, view.Best))
                best.ForeColor = Theme.Green;

            if (!displayedAttempts.SequenceEqual(view.Recent) || displayedBest != view.Best)
            {
                history.SetRuns(view.Recent, view.Best);
                displayedAttempts = view.Recent;
                displayedBest = view.Best;
            }
            bool hasAttempts = view.Recent.Length != 0;
            history.Visible = hasAttempts;
            emptyHistory.Visible = !hasAttempts;
            emptyHistory.Text = view.Finish.HasValue ? "Cross both timing markers to record a run." :
                view.Start.HasValue ? "In ACC, press Ctrl+Shift+L after the exit to mark the finish." :
                "Select Place on map to click the markers onto the track, or press Ctrl+Shift+K and Ctrl+Shift+L in ACC.";
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
            card.BackColor = Theme.Surface;
            card.Margin = new Padding(0, 4, 12, 4);
            card.Padding = new Padding(12, 5, 8, 3);
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            card.Controls.Add(SectionHeading(name), 0, 0);
            card.Controls.Add(value, 0, 1);
            return card;
        }

        private static Label Caption(string text, float size)
        {
            return new Label
            {
                Text = text, Font = new Font("Segoe UI", size), Dock = DockStyle.Fill,
                ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
                AutoEllipsis = true, UseMnemonic = false
            };
        }

        private static Label SectionHeading(string text)
        {
            var label = Caption(text.ToUpperInvariant(), 8.5f);
            label.Font = new Font("Segoe UI Semibold", 8.5f);
            label.ForeColor = Theme.Muted;
            label.TextAlign = ContentAlignment.BottomLeft;
            label.Padding = new Padding(0, 0, 0, 5);
            return label;
        }
    }
}
