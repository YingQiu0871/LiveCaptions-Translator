using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                AppHost.Start();
                desktop.MainWindow = new MainWindow();
                desktop.Exit += (s, e) => AppHost.Shutdown();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
