using System;
using System.Windows.Forms;

namespace PongWinForms
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Default settings (can be loaded from user settings if desired)
            var defaults = new GameSettings();

            using var dlg = new SettingsForm(defaults);
            var result = dlg.ShowDialog();

            if (result != DialogResult.OK || dlg.Result == null)
                return; // User cancelled

            // Start the game with selected settings
            Application.Run(new MainForm(dlg.Result));
        }
    }
}