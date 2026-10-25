using System;
using System.Drawing;
using System.Windows.Forms;

namespace Moonlight
{
    internal interface ISettingsActions
    {
        Settings Current { get; }
        Localizer Texts { get; }
        bool StartupEnabled { get; }
        void Save(Settings settings, bool runAtLogin);
        void Manual(ThemeMode mode, bool notify);
        void Resume();
        void ChangeLanguage(string language);
    }

    internal sealed class SettingsForm : Form
    {
        private readonly ISettingsActions owner;
        private readonly CheckBox enabled, windowsTheme, startup;
        private readonly DateTimePicker lightTime, darkTime;
        private readonly Label lightCaption, darkCaption, feedback;
        private readonly Button lightButton, darkButton, resumeButton, saveButton, hideButton;
        private readonly ComboBox languageSelector;
        private readonly ToolTip tips = new ToolTip();
        private string feedbackKey;
        private object[] feedbackArgs = new object[0];
        private bool feedbackError, updatingLanguage, darkPalette;
        public bool AllowExit;

        public SettingsForm(ISettingsActions owner, Icon icon)
        {
            this.owner = owner;
            Name = "settingsForm"; Text = "MOONLIGHT"; Icon = icon;
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(580, 550); MinimumSize = new Size(550, 450);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false;
            var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Name = "settingsLayout", Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(26, 20, 26, 20), ColumnCount = 1, RowCount = 8 };
            for (int row = 0; row < 8; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewport.Controls.Add(layout); Controls.Add(viewport);
            layout.Controls.Add(new Label { Name = "brandName", Text = "MOONLIGHT", Font = new Font("Segoe UI", 25F, FontStyle.Bold),
                Dock = DockStyle.Top, Height = 55, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            enabled = new CheckBox { Name = "enableSchedule", AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
            layout.Controls.Add(enabled, 0, 1);

            var times = new TableLayoutPanel { Name = "startTimes", ColumnCount = 2, RowCount = 2, Dock = DockStyle.Top,
                Height = 86, Margin = new Padding(0, 0, 0, 12) };
            times.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F)); times.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            lightCaption = Caption("lightCaption"); darkCaption = Caption("darkCaption");
            times.Controls.Add(lightCaption, 0, 0); times.Controls.Add(darkCaption, 1, 0);
            lightTime = Picker("lightStartTime"); darkTime = Picker("darkStartTime");
            times.Controls.Add(lightTime, 0, 1); times.Controls.Add(darkTime, 1, 1);
            layout.Controls.Add(times, 0, 2);

            var options = new FlowLayoutPanel { Name = "themeOptions", Dock = DockStyle.Top, AutoSize = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
            windowsTheme = new CheckBox { Name = "switchWindowsTheme", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            startup = new CheckBox { Name = "startWithWindows", AutoSize = true, Margin = new Padding(0) };
            options.Controls.Add(windowsTheme); options.Controls.Add(startup);
            layout.Controls.Add(options, 0, 3);

            var quick = Row("quickActions", 3);
            lightButton = ButtonOf("switchLightButton", delegate { owner.Manual(ThemeMode.Light, false); }, false);
            darkButton = ButtonOf("switchDarkButton", delegate { owner.Manual(ThemeMode.Dark, false); }, false);
            resumeButton = ButtonOf("resumeScheduleButton", delegate { owner.Resume(); }, false);
            quick.Controls.Add(lightButton, 0, 0); quick.Controls.Add(darkButton, 1, 0); quick.Controls.Add(resumeButton, 2, 0);
            quick.Margin = new Padding(0, 0, 0, 12); layout.Controls.Add(quick, 0, 4);

            var actions = Row("mainActions", 3);
            actions.ColumnStyles.Clear();
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            saveButton = ButtonOf("saveButton", SaveSettings, true);
            languageSelector = new ComboBox { Name = "languageSelector", DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 0), FlatStyle = FlatStyle.Flat };
            languageSelector.Items.AddRange(new object[] { "English", "简体中文" });
            languageSelector.SelectedIndexChanged += delegate
            {
                if (!updatingLanguage && languageSelector.SelectedIndex >= 0)
                    owner.ChangeLanguage(languageSelector.SelectedIndex == 1 ? "zh-CN" : "en");
            };
            hideButton = ButtonOf("hideButton", delegate { Hide(); }, false);
            actions.Controls.Add(saveButton, 0, 0); actions.Controls.Add(languageSelector, 1, 0); actions.Controls.Add(hideButton, 2, 0);
            actions.Margin = new Padding(0, 0, 0, 8); layout.Controls.Add(actions, 0, 5);

            feedback = new Label { Name = "feedback", AutoSize = true, MaximumSize = new Size(528, 0),
                MinimumSize = new Size(0, 28), Margin = new Padding(0, 4, 0, 0) };
            layout.Controls.Add(feedback, 0, 6);
            AcceptButton = saveButton;
            LoadFields(); ApplyLanguage(); ApplyPalette(!WindowsTheme.IsLight("AppsUseLightTheme"));
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!AllowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        private static Label Caption(string name)
        {
            return new Label { Name = name, Dock = DockStyle.Top, Height = 27, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Margin = Padding.Empty };
        }
        private static DateTimePicker Picker(string name)
        {
            return new DateTimePicker { Name = name, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
                Width = 180, Font = new Font("Segoe UI", 15F), Margin = new Padding(0, 2, 0, 6) };
        }
        private static TableLayoutPanel Row(string name, int columns)
        {
            var row = new TableLayoutPanel { Name = name, Dock = DockStyle.Top, ColumnCount = columns, RowCount = 1, Height = 42, Margin = Padding.Empty };
            for (int col = 0; col < columns; col++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            return row;
        }
        private static Button ButtonOf(string name, EventHandler action, bool primary)
        {
            var button = new Button { Name = name, Dock = DockStyle.Fill, Padding = new Padding(4, 2, 4, 2),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), Tag = primary ? "primary" : null };
            button.Click += action; return button;
        }
        public void LoadFields()
        {
            Settings value = owner.Current;
            enabled.Checked = value.Enabled; windowsTheme.Checked = value.SwitchWindowsTheme; startup.Checked = owner.StartupEnabled;
            lightTime.Value = DateTime.Today.AddMinutes(value.LightMinute);
            darkTime.Value = DateTime.Today.AddMinutes(value.DarkMinute);
        }
        public void ApplyLanguage()
        {
            Localizer text = owner.Texts;
            SuspendLayout();
            enabled.Text = text["schedule.enable"]; lightCaption.Text = text["time.light"]; darkCaption.Text = text["time.dark"];
            lightTime.AccessibleName = lightCaption.Text; darkTime.AccessibleName = darkCaption.Text;
            windowsTheme.Text = text["theme.windows"]; startup.Text = text["startup"];
            lightButton.Text = text["switch.light"]; darkButton.Text = text["switch.dark"]; resumeButton.Text = text["schedule.resume"];
            saveButton.Text = text["save"]; hideButton.Text = text["hide"]; languageSelector.AccessibleName = text["language"];
            tips.SetToolTip(windowsTheme, text["theme.hint"]); tips.SetToolTip(languageSelector, text["language"]);
            updatingLanguage = true;
            try { languageSelector.SelectedIndex = text.Language == "zh-CN" ? 1 : 0; }
            finally { updatingLanguage = false; }
            RefreshFeedback(); ResumeLayout(true);
        }
        private void SaveSettings(object sender, EventArgs e)
        {
            Settings next = owner.Current.Copy();
            next.LightMinute = lightTime.Value.Hour * 60 + lightTime.Value.Minute;
            next.DarkMinute = darkTime.Value.Hour * 60 + darkTime.Value.Minute;
            next.Enabled = enabled.Checked; next.SwitchWindowsTheme = windowsTheme.Checked;
            owner.Save(next, startup.Checked);
        }
        public void SetFeedback(string key, bool error, params object[] args)
        {
            feedbackKey = key; feedbackArgs = args; feedbackError = error; RefreshFeedback();
        }
        private void RefreshFeedback()
        {
            feedback.Text = feedbackKey == null ? "" : owner.Texts.Format(feedbackKey, feedbackArgs);
            feedback.ForeColor = feedbackError ? Color.IndianRed : darkPalette ? Color.LightGreen : Color.FromArgb(25, 112, 72);
        }
        public void RefreshStatus(string statusKey, bool dark)
        {
            if (dark != darkPalette) ApplyPalette(dark);
            if (feedbackKey == null || feedbackKey.StartsWith("status.", StringComparison.Ordinal)) SetFeedback(statusKey, false);
        }
        private void ApplyPalette(bool dark)
        {
            darkPalette = dark;
            Color background = dark ? Color.FromArgb(26, 29, 36) : Color.FromArgb(248, 249, 252);
            Color foreground = dark ? Color.FromArgb(231, 235, 244) : Color.FromArgb(31, 39, 53);
            PaintControls(this, background, foreground, dark); RefreshFeedback();
            if (IsHandleCreated)
            {
                int value = dark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int));
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int value = darkPalette ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int));
        }
        private static void PaintControls(Control root, Color background, Color foreground, bool dark)
        {
            root.BackColor = background; root.ForeColor = foreground;
            foreach (Control child in root.Controls)
            {
                PaintControls(child, background, foreground, dark);
                Button button = child as Button;
                if (button != null)
                {
                    bool primary = (string)button.Tag == "primary";
                    button.BackColor = primary ? Color.FromArgb(67, 100, 220) : dark ? Color.FromArgb(44, 49, 62) : Color.White;
                    button.ForeColor = primary ? Color.White : foreground;
                    button.FlatAppearance.BorderColor = primary ? button.BackColor : dark ? Color.FromArgb(72, 81, 99) : Color.FromArgb(211, 217, 229);
                }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose(); base.Dispose(disposing);
        }
    }
}
