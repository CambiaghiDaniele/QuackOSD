using System.Windows;

namespace QuackOSD
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        // Questo metodo ora sostituisce il "StartupUri" che abbiamo cancellato
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            //create OSD windows
            var osdWindow = new OsdWindow();
            //create setting windows
            var settingsWindows = new SettingsWindow();

            //create main windows (Logic)
            var mainWindow = new MainWindow(osdWindow, settingsWindows);
        }
    }
}