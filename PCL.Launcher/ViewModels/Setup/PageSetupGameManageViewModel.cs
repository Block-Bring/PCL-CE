using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI.Dialogs;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>设置页面中「游戏管理」页的视图模型。</summary>
public partial class PageSetupGameManageViewModel : ViewModelBase
{
    [ObservableProperty] private int _compSourceIndex = Math.Max(
        0,
        Config.Download.Comp.CompSourceSolution);

    [ObservableProperty] private int _fileSourceIndex = Math.Max(0, Config.Download.FileSource);

    [ObservableProperty] private int _httpModeIndex = Math.Max(0, (int)Config.Download.HttpMode);

    [ObservableProperty] private int _localNameStyleIndex = Math.Max(
        0,
        Config.Download.Comp.UiCompNameSolution);

    [ObservableProperty] private int _quickBehaviorIndex = Math.Max(
        0,
        Config.Download.Comp.QuickDownloadBehavior);

    [ObservableProperty] private string _curseForgeApiKey = Config.Network.CurseForgeApiKey;

    [ObservableProperty] private int _defaultFilterModeModIndex = Math.Max(
        0,
        Config.Download.Comp.DefaultFilterModeMod);

    [ObservableProperty] private int _defaultFilterModeDataPackIndex = Math.Max(
        0,
        Config.Download.Comp.DefaultFilterModeDataPack);

    [ObservableProperty] private int _defaultFilterModeResourcePackIndex = Math.Max(
        0,
        Config.Download.Comp.DefaultFilterModeResourcePack);

    [ObservableProperty] private int _defaultFilterModeShaderIndex = Math.Max(
        0,
        Config.Download.Comp.DefaultFilterModeShader);

    [ObservableProperty] private int _defaultFilterModeWorldIndex = Math.Max(
        0,
        Config.Download.Comp.DefaultFilterModeWorld);

    [ObservableProperty]
    private int _translateFormatIndex = Math.Max(0, Config.Download.Comp.NameFormatV2);

    [ObservableProperty] private int _versionListIndex = Math.Max(
        0,
        Config.Download.VersionListSource);

    public bool FileFormatSettingsVisible => Lang.IsChineseMainland;

    partial void OnHttpModeIndexChanged(int value)
    {
        if (value >= 0) Config.Download.HttpMode = (DownloadHttpMode)value;
    }

    partial void OnFileSourceIndexChanged(int value)
    {
        if (value >= 0) Config.Download.FileSource = value;
    }

    partial void OnVersionListIndexChanged(int value)
    {
        if (value >= 0) Config.Download.VersionListSource = value;
    }

    partial void OnTranslateFormatIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.NameFormatV2 = value;
    }

    partial void OnCompSourceIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.CompSourceSolution = value;
    }

    partial void OnLocalNameStyleIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.UiCompNameSolution = value;
    }

    partial void OnQuickBehaviorIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.QuickDownloadBehavior = value;
    }

    partial void OnDefaultFilterModeModIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.DefaultFilterModeMod = value;
    }

    partial void OnDefaultFilterModeDataPackIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.DefaultFilterModeDataPack = value;
    }

    partial void OnDefaultFilterModeResourcePackIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.DefaultFilterModeResourcePack = value;
    }

    partial void OnDefaultFilterModeShaderIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.DefaultFilterModeShader = value;
    }

    partial void OnDefaultFilterModeWorldIndexChanged(int value)
    {
        if (value >= 0) Config.Download.Comp.DefaultFilterModeWorld = value;
    }

    partial void OnCurseForgeApiKeyChanged(string value)
    {
        Config.Network.CurseForgeApiKey = value ?? string.Empty;
    }

    public void Reload()
    {
        HttpModeIndex = Math.Clamp((int)Config.Download.HttpMode, 0, 2);
        FileSourceIndex = Math.Clamp(Config.Download.FileSource, 0, 2);
        VersionListIndex = Math.Clamp(Config.Download.VersionListSource, 0, 2);
        TranslateFormatIndex = Math.Clamp(Config.Download.Comp.NameFormatV2, 0, 4);
        CompSourceIndex = Math.Clamp(Config.Download.Comp.CompSourceSolution, 0, 2);
        LocalNameStyleIndex = Math.Clamp(Config.Download.Comp.UiCompNameSolution, 0, 1);
        QuickBehaviorIndex = Math.Clamp(Config.Download.Comp.QuickDownloadBehavior, 0, 3);
        DefaultFilterModeModIndex = Math.Clamp(Config.Download.Comp.DefaultFilterModeMod, 0, 1);
        DefaultFilterModeDataPackIndex = Math.Clamp(
            Config.Download.Comp.DefaultFilterModeDataPack,
            0,
            1);
        DefaultFilterModeResourcePackIndex = Math.Clamp(
            Config.Download.Comp.DefaultFilterModeResourcePack,
            0,
            1);
        DefaultFilterModeShaderIndex = Math.Clamp(
            Config.Download.Comp.DefaultFilterModeShader,
            0,
            1);
        DefaultFilterModeWorldIndex = Math.Clamp(Config.Download.Comp.DefaultFilterModeWorld, 0, 1);
        CurseForgeApiKey = Config.Network.CurseForgeApiKey;
    }

    /// <summary>首次把下载线程调到 64 及以上时给出警告。</summary>
    public async Task WarnLargeThreadOnceAsync()
    {
        if (States.Hint.LargeDownloadThread) return;
        States.Hint.LargeDownloadThread = true;
        await Dialog.ShowAsync(
            new TextDialogRequest
            {
                Title = Lang.Text("Common.Dialog.Warning"),
                Content = Lang.Text("Setup.GameManage.Download.Threads.TooManyWarning.Message"),
                Buttons = DialogButtons.From(
                    DialogButton.Create(
                        Lang.Text("Setup.GameManage.Download.Threads.TooManyWarning.Confirm"))),
                IsWarn = true
            });
    }
}