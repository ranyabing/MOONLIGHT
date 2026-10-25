using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Moonlight
{
    internal sealed class PromoActions : ISettingsActions
    {
        public Settings Current { get; private set; }
        public Localizer Texts { get; private set; }
        public bool StartupEnabled { get { return false; } }
        public SettingsForm Form;
        public PromoActions(string language)
        {
            Current = new Settings(); Current.Language = language;
            Texts = new Localizer(language);
        }
        public void Save(Settings settings, bool runAtLogin) { Current = settings.Copy(); }
        public void Manual(ThemeMode mode, bool notify) { }
        public void Resume() { }
        public void ChangeLanguage(string language)
        {
            Current.Language = language; Texts.SetLanguage(language);
            if (Form != null) Form.ApplyLanguage();
        }
    }

    internal static class PromoCapture
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("Expected executable and output directory.");
            string executable = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Icon icon = Icon.ExtractAssociatedIcon(executable))
            {
                foreach (string language in new[] { "zh-CN", "en" })
                {
                    foreach (bool dark in new[] { false, true })
                    {
                        var owner = new PromoActions(language);
                        using (var form = new SettingsForm(owner, icon))
                        {
                            owner.Form = form;
                            form.StartPosition = FormStartPosition.Manual;
                            form.Location = new Point(40, 40);
                            form.RefreshStatus("status.schedule", dark);
                            form.Show();
                            form.Activate();
                            Application.DoEvents();
                            form.PerformLayout();
                            form.Refresh();
                            Application.DoEvents();
                            string filename = "moonlight-" + language + "-" + (dark ? "dark" : "light") + ".png";
                            using (var bitmap = new Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                                bitmap.Save(Path.Combine(output, filename), ImageFormat.Png);
                            }
                            Console.WriteLine(filename + " " + form.Width + "x" + form.Height);
                            form.AllowExit = true;
                            form.Close();
                        }
                    }
                }
            }
        }
    }
}
