using System.Windows;
using Achikobuddy.Memory;

namespace Achikobuddy.Core
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Bugger.Log("App started [Critical]");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Bugger.Log("App exiting [Critical]");
            base.OnExit(e);
        }
    }
}