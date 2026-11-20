using Avalonia;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using PCL.Core.App;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.UI.Controls;
using PCL.UI.Controls.Behaviors;
using PCL.UI.Diagnostics;
using PCL.UI.Theming;

namespace PCL;

public class App : Application
{
    public override void Initialize()
    {
#if DEBUG
        Logger.Sink = new BindingLogSink();
#endif
        AvaloniaXamlLoader.Load(this);
        // 自建构建没有可用的联机服务器，强制隐藏「设置-联机」与「工具-大厅」
        // 配置服务在 Loading 阶段才初始化，此处只注册回 hook，不能直接访问 Config
        Lifecycle.When(
            LifecycleState.Running,
            () =>
            {
                Config.Preference.Hide.SetupGameLink = true;
                Config.Preference.Hide.ToolsGameLink = true;
            });
        LaunchFonts.InstallGlobalFont();
        LocalizationService.LanguageChanged += LaunchFonts.InstallGlobalFont;
        Lifecycle.When(
            LifecycleState.WindowCreated,
            () =>
            {
                if (Lifecycle.CurrentApplication?.MainWindow is IAvaloniaWindowHost window)
                    PlatformStartup.Shell.OnWindowCreated(window.Window);
            });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ThemeCoordinator.Install();
        Tooltip.Enable();
        ClipboardInterceptor.Enable();
        PlatformStartup.Shell.OnFrameworkInitialized(this);

        if (Lifecycle.CurrentApplication is IAvaloniaApplicationHost host)
            host.Attach(this);
        base.OnFrameworkInitializationCompleted();
    }
}