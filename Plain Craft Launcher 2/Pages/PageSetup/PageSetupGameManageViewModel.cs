using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Core.App;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>设置页面中「游戏管理」页的视图模型。</summary>
public partial class PageSetupGameManageViewModel : ViewModelBase
{
    [ObservableProperty] private int _compSourceIndex = Math.Max(0, Config.Download.Comp.CompSourceSolution);

    [ObservableProperty] private int _fileSourceIndex = Math.Max(0, Config.Download.FileSource);

    [ObservableProperty] private int _httpModeIndex = Math.Max(0, (int)Config.Download.HttpMode);

    [ObservableProperty] private int _localNameStyleIndex = Math.Max(0, Config.Download.Comp.UiCompNameSolution);

    [ObservableProperty] private int _quickBehaviorIndex = Math.Max(0, Config.Download.Comp.QuickDownloadBehavior);

    [ObservableProperty] private int _defaultFilterModeModIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeMod);

    [ObservableProperty] private int _defaultFilterModeDataPackIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeDataPack);

    [ObservableProperty] private int _defaultFilterModeResourcePackIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeResourcePack);

    [ObservableProperty] private int _defaultFilterModeShaderIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeShader);

    [ObservableProperty] private int _defaultFilterModeWorldIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeWorld);

    [ObservableProperty] private int _translateFormatIndex = Math.Max(0, Config.Download.Comp.NameFormatV2);

    [ObservableProperty] private int _versionListIndex = Math.Max(0, Config.Download.VersionListSource);

    [ObservableProperty] private string _curseForgeApiKey = Config.Network.CurseForgeApiKey;

    public void Reload()
    {
        HttpModeIndex = Math.Max(0, (int)Config.Download.HttpMode);
        FileSourceIndex = Math.Max(0, Config.Download.FileSource);
        VersionListIndex = Math.Max(0, Config.Download.VersionListSource);
        TranslateFormatIndex = Math.Max(0, Config.Download.Comp.NameFormatV2);
        CompSourceIndex = Math.Max(0, Config.Download.Comp.CompSourceSolution);
        LocalNameStyleIndex = Math.Max(0, Config.Download.Comp.UiCompNameSolution);
        QuickBehaviorIndex = Math.Max(0, Config.Download.Comp.QuickDownloadBehavior);
        DefaultFilterModeModIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeMod);
        DefaultFilterModeDataPackIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeDataPack);
        DefaultFilterModeResourcePackIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeResourcePack);
        DefaultFilterModeShaderIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeShader);
        DefaultFilterModeWorldIndex = Math.Max(0, Config.Download.Comp.DefaultFilterModeWorld);
        CurseForgeApiKey = Config.Network.CurseForgeApiKey;
    }
}
