using System;
using System.Windows.Forms;

namespace PongWinForms
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // High-DPI aware app (Win10/11) – must be first
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Load persisted settings
            var defaults = SettingsIO.Load();

            // Show settings dialog first
            using var dlg = new SettingsForm(defaults);
            var result = dlg.ShowDialog();
            if (result != DialogResult.OK || dlg.Result == null) return;

            // Save chosen settings
            SettingsIO.Save(dlg.Result);

            // Start game with chosen settings
            Application.Run(new MainForm(dlg.Result));
        }
    }
}