using System.Windows;

namespace TrafficSim
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            new MainMenu().Show();
        }
    }
}
