using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CursorMeter.App;

public sealed partial class App : Application
{
    private MeterController? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppLog.HookUnhandledErrors();
        AppLog.Info($"Start {AppInfo.Version}, {AppInfo.Platform}");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _controller = new MeterController(desktop);
            desktop.Exit += (_, _) =>
            {
                AppLog.Info("Exit");
                _controller.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
