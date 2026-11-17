using PCL.Core.App;
using PCL.Core.App.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace PCL;

[ContentProperty("SearchTags")]
public partial class PageComp
{
    /// <summary>
    ///     每页展示的结果数量。
    /// </summary>
    public const int PageSize = 40;

    public int page;

    public Comp.CompProjectStorage storage = new();

    public PageCompViewModel ViewModel { get; } = new();

    /// <summary>
    ///     列表区域的滚动控件。外层页面通过 MyPageRight.PanScroll 绑定它，以支持「返回顶部」。
    /// </summary>
    public MyScrollViewer ListScroll => this;

    private void Load_OnFinish()
    {
        try
        {
            Common.Log($"[Comp] 开始可视化{TypeNameSpaced}列表，已储藏 {storage.results.Count} 个结果，当前在第 {page + 1} 页");

            var startIndex = page * PageSize;
            var count = startIndex >= storage.results.Count
                ? 0
                : Math.Min(storage.results.Count - startIndex, PageSize);
            var items = new List<MyCompItemViewModel>(count);
            if (count > 0)
            {
                var showMcVersionDesc = loader.input!.gameVersion is null;
                var showLoaderDesc = loader.input.modLoader == Comp.CompLoaderType.Any &&
                                     (PageType == Comp.CompType.Mod || PageType == Comp.CompType.ModPack);
                foreach (var result in storage.results.GetRange(startIndex, count))
                {
                    var showQuickDownload = result.Type != Comp.CompType.ModPack &&
                                            result.Type != Comp.CompType.DataPack;
                    items.Add(result.ToCompItemViewModel(showMcVersionDesc, showLoaderDesc, showQuickDownload,
                        _requestInstance));
                }
            }

            ViewModel.ReplaceProjects(items);
            // 页码
            ViewModel.ApplyPagerState(page, storage);
            // 错误信息
            ViewModel.ApplyErrorState(storage);

            // 强制返回顶部
            ScrollToTop();
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                $"可视化{TypeNameSpaced}列表出错",
                Common.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Comp.Error.OperationFailed"));
        }
    }

    private void Load_State(object sender, LoadingState state, LoadingState oldState)
    {
        switch (loader.State)
        {
            case LoadState.Failed:
                {
                    var errorMessage = "";
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    // this maybe a wrong, Exception will be null
                    if (loader.Error is not null)
                        errorMessage = loader.Error.Message;
                    if (errorMessage.Contains(Lang.Text("Common.Error.InvalidJson")))
                    {
                        Common.Log($"[Download] 下载的{TypeNameSpaced}列表 json 文件损坏，已自动重试", Common.LogLevel.Debug);
                        ((MyPageRight)Parent).PageLoaderRestart();
                    }

                    break;
                }
        }
    }

    // 切换页码
    private void BtnPageFirst_Click(object sender, EventArgs e)
    {
        ChangePage(0);
    }

    private void BtnPageLeft_Click(object sender, EventArgs e)
    {
        ChangePage(page - 1);
    }

    private void BtnPageRight_Click(object sender, EventArgs e)
    {
        ChangePage(page + 1);
    }

    private async void ChangePage(int newPage)
    {
        CardPages.IsEnabled = false;
        page = newPage;
        Launcher.frmMain!.BackToTop();
        Common.Log($"[Download] {TypeName}：切换到第 {page + 1} 页");
        // 等待返回顶部的平滑滚动动画结束，避免加载完成后滚动位置错乱
        await Task.Delay(150).ConfigureAwait(true);
        CardPages.IsEnabled = true;
        loader.Start();
    }

    /// <summary>
    ///     刷新所有已显示项目的收藏状态
    /// </summary>
    public void RefreshAllFavoriteStatus()
    {
        try
        {
            ViewModel.RefreshAllFavoriteStatus();
        }
        catch (Exception ex)
        {
            Common.Log(ex, "刷新收藏状态时出错");
        }
    }

    #region 属性

    /// <summary>
    ///     用于 XAML 快速设置的 Tag 下拉框列表。
    /// </summary>
    public ItemCollection SearchTags => ComboSearchTag.Items;

    public static readonly DependencyProperty SupportCurseForgeProperty =
        DependencyProperty.Register(nameof(SupportCurseForge), typeof(bool), typeof(PageComp),
            new PropertyMetadata(true, OnSupportCurseForgeChanged));

    public bool SupportCurseForge
    {
        get => (bool)GetValue(SupportCurseForgeProperty);
        set => SetValue(SupportCurseForgeProperty, value);
    }

    public static readonly DependencyProperty SupportModrinthProperty =
        DependencyProperty.Register(nameof(SupportModrinth), typeof(bool), typeof(PageComp),
            new PropertyMetadata(true, OnSupportModrinthChanged));

    public bool SupportModrinth
    {
        get => (bool)GetValue(SupportModrinthProperty);
        set => SetValue(SupportModrinthProperty, value);
    }

    private static void OnSupportCurseForgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PageComp)d).ViewModel.SupportCurseForge = (bool)e.NewValue;

    private static void OnSupportModrinthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PageComp)d).ViewModel.SupportModrinth = (bool)e.NewValue;

    /// <summary>
    ///     英文前后不含空格的可读资源类型名，例如 "Mod"、"整合包"。
    /// </summary>
    public string TypeName => Comp.GetCompTypeName(PageType);

    /// <summary>
    ///     英文前后含一个空格的可读资源类型名，例如 " Mod "、"整合包"。
    /// </summary>
    public string TypeNameSpaced => TypeName;

    /// <summary>
    ///     该页面对应的资源类型。
    /// </summary>
    public Comp.CompType PageType
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            ViewModel.IsInstallModPackVisible = value == Comp.CompType.ModPack;
            loader.name = Lang.Text("Download.Comp.List.Source.ResourceFetch", TypeName);
            PanSearchBox.HintText = Comp.GetCompSearchName(value);
            ViewModel.LoadingText = Comp.GetCompLoadingName(value);
        }
    } = (Comp.CompType)(-1);

    #endregion

    #region 加载

    /// <summary>
    ///     在切换到页面时，应自动将筛选项设置为与该目标 MC 版本和加载器相同。
    /// </summary>
    public static McInstance? targetVersion;

    public Loading.LoaderTask<Comp.CompProjectRequest, int> loader;

    private bool _isLoaderInited;

    public PageComp()
    {
        loader = new Loading.LoaderTask<Comp.CompProjectRequest, int>(Lang.Text("Download.Comp.List.Source.ResourceFetch", "XXX"), Comp.CompProjectsGet,
            inputDelegate: LoaderInput)
        { reloadTimeout = 60 * 1000 };
        Loaded += PageCompControls_Inited;
        IsVisibleChanged += PageComp_IsVisibleChanged;
        InitializeComponent();
        DataContext = ViewModel;
        InstanceList.mcInstanceListLoader.OnStateChangedUi += InstanceList_OnStateChanged;
    }

    private void PanSearchBox_Search(object sender, EventArgs e) => StartNewSearch();

    private void PageCompControls_Inited(object sender, EventArgs e)
    {
        // 首次加载时按设置初始化版本筛选方式
        if (!_isLoaderInited)
        {
            ComboSearchVersionMode.SelectedIndex = GetDefaultFilterMode();
            UpdateFilterControlVisibility();
            if (IsInstanceFilterMode)
            {
                EnsureInstanceListLoaded();
                RefreshInstanceCombo(false);
            }
        }

        // 不知道从 Initialized 改成 Loaded 会不会有问题，但用 Initialized 会导致初始的筛选器修改被覆盖回默认值
        if (targetVersion is not null)
        {
            // 设置目标
            ResetFilter(); // 重置筛选器
            ComboSearchVersionMode.SelectedIndex = 0; // 从实例页跳转时保持手动选择
            UpdateFilterControlVisibility();
            TextSearchVersion.Text = targetVersion.Info.VanillaName;

            MyComboBoxItem GetTargetItemByName(string name)
            {
                foreach (MyComboBoxItem item in ComboSearchLoader.Items)
                    if (string.Equals(item.Content?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        return item;
                return (MyComboBoxItem)ComboSearchLoader.Items[0]!;
            }

            ;
            if (targetVersion.Info.HasForge)
                ComboSearchLoader.SelectedItem = GetTargetItemByName("Forge");
            else if (targetVersion.Info.HasFabric)
                ComboSearchLoader.SelectedItem = GetTargetItemByName("Fabric");
            else if (targetVersion.Info.HasNeoForge)
                ComboSearchLoader.SelectedItem = GetTargetItemByName("NeoForge");
            else if (targetVersion.Info.HasQuilt) ComboSearchLoader.SelectedItem = GetTargetItemByName("Quilt");
            targetVersion = null;
            // 如果已经完成请求，则重新开始
            if (_isLoaderInited)
                StartNewSearch();
            ScrollToHome();
        }

        // 加载器初始化
        if (_isLoaderInited)
            return;
        _isLoaderInited = true;
        ((MyPageRight)Parent).PageLoaderInit(Load, PanLoad, PanContent, PanAlways, loader, _ => Load_OnFinish(),
            LoaderInput);

        if (GameDownload.AllDrops is not null && GameDownload.AllDrops.Count != 0 && GameDownload.AllDrops[0] > 250)
        {
            var highestVersion = McInstanceInfo.DropToVersion(GameDownload.AllDrops[0]);
            if ((((MyComboBoxItem)TextSearchVersion.Items[1]!).Content.ToString() ?? "") !=
                (highestVersion ?? "")) // 0 是全部
                TextSearchVersion.Items.Insert(1, new MyComboBoxItem { Content = highestVersion });
        }

    }

    private void PageComp_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) RefreshAllFavoriteStatus();
    }

    /// <summary>
    ///     当前是否使用“按实例”筛选（整合包页不支持该模式）。
    /// </summary>
    private bool IsInstanceFilterMode => PageType != Comp.CompType.ModPack &&
                                         ComboSearchVersionMode.SelectedIndex == 1;

    private int GetDefaultFilterMode()
    {
        var mode = PageType switch
        {
            Comp.CompType.Mod => Config.Download.Comp.DefaultFilterModeMod,
            Comp.CompType.DataPack => Config.Download.Comp.DefaultFilterModeDataPack,
            Comp.CompType.ResourcePack => Config.Download.Comp.DefaultFilterModeResourcePack,
            Comp.CompType.Shader => Config.Download.Comp.DefaultFilterModeShader,
            Comp.CompType.World => Config.Download.Comp.DefaultFilterModeWorld,
            _ => 0
        };
        return Math.Clamp(mode, 0, 1);
    }

    private void UpdateFilterControlVisibility()
    {
        var isInstanceMode = IsInstanceFilterMode;
        var supportsMode = PageType != Comp.CompType.ModPack;

        ViewModel.IsVersionModeVisible = supportsMode;
        ViewModel.IsVersionComboVisible = !isInstanceMode;
        ViewModel.IsInstanceComboVisible = isInstanceMode;
        ViewModel.IsLoaderLabelVisible = !isInstanceMode &&
                                         PageType is Comp.CompType.Shader or Comp.CompType.Mod
                                             or Comp.CompType.ModPack;
        ViewModel.IsLoaderComboVisible = !isInstanceMode &&
                                         PageType is Comp.CompType.Mod or Comp.CompType.ModPack;
        ViewModel.IsShaderLoaderComboVisible = !isInstanceMode && PageType == Comp.CompType.Shader;

        // 整合包页没有版本筛选方式，沿用原来的列位置
        var versionColumn = supportsMode ? 2 : 0;
        var loaderColumn = supportsMode ? 4 : 2;
        Grid.SetColumn(LabVersion, versionColumn);
        Grid.SetColumn(TextSearchVersion, versionColumn + 1);
        Grid.SetColumn(LabInstance, versionColumn);
        Grid.SetColumn(ComboSearchInstance, versionColumn + 1);
        Grid.SetColumn(LabLoader, loaderColumn);
        Grid.SetColumn(ComboSearchLoader, loaderColumn + 1);
        Grid.SetColumn(ComboSearchShaderLoader, loaderColumn + 1);
    }

    private void ComboSearchVersionMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateFilterControlVisibility();
        if (!IsInstanceFilterMode)
            return;
        EnsureInstanceListLoaded();
        RefreshInstanceCombo(true);
    }

    private void InstanceList_OnStateChanged(Loading.LoaderBase sender, LoadState state, LoadState oldState)
    {
        if (state != LoadState.Finished || !ViewModel.IsInstanceComboVisible)
            return;
        RefreshInstanceCombo(true);
    }

    private static void EnsureInstanceListLoaded()
    {
        if (string.IsNullOrEmpty(GameFolders.mcFolderSelected))
            return;
        Loading.LoaderFolderRun(
            InstanceList.mcInstanceListLoader,
            GameFolders.mcFolderSelected,
            Loading.LoaderFolderRunType.RunOnUpdated,
            1,
            @"versions\");
    }

    /// <summary>
    ///     按当前文件夹重新填充实例下拉框，优先保持原选择，其次选中当前实例。
    /// </summary>
    private void RefreshInstanceCombo(bool keepSelection)
    {
        var previous = keepSelection
            ? (ComboSearchInstance.SelectedItem as MyComboBoxItem)?.Tag as McInstance
            : null;
        var instances = InstanceList.mcInstanceList.Values
            .SelectMany(list => list)
            .Where(instance => instance is not null && instance.state != McInstanceState.Error)
            .OrderBy(instance => instance.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ComboSearchInstance.Items.Clear();
        foreach (var instance in instances)
            ComboSearchInstance.Items.Add(new MyComboBoxItem { Content = instance.Name, Tag = instance });

        var selected = previous is null
            ? null
            : instances.FirstOrDefault(instance => instance.PathInstance == previous.PathInstance);
        if (selected is null && InstanceList.McMcInstanceSelected is { } current)
            selected = instances.FirstOrDefault(instance => instance.PathInstance == current.PathInstance);
        selected ??= instances.FirstOrDefault();
        ComboSearchInstance.SelectedItem = ComboSearchInstance.Items
            .OfType<MyComboBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, selected));
    }

    private void SelectCurrentFilterInstance()
    {
        if (InstanceList.McMcInstanceSelected is not { } current)
            return;
        ComboSearchInstance.SelectedItem = ComboSearchInstance.Items
            .OfType<MyComboBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, current));
    }

    private McInstance? GetSelectedFilterInstance()
        => (ComboSearchInstance.SelectedItem as MyComboBoxItem)?.Tag as McInstance;

    private static Comp.CompLoaderType GetInstanceModLoader(McInstance instance)
    {
        if (!instance.IsLoaded)
            instance.Load();
        if (instance.Info.HasForge)
            return Comp.CompLoaderType.Forge;
        if (instance.Info.HasFabric)
            return Comp.CompLoaderType.Fabric;
        if (instance.Info.HasNeoForge)
            return Comp.CompLoaderType.NeoForge;
        if (instance.Info.HasQuilt)
            return Comp.CompLoaderType.Quilt;
        return Comp.CompLoaderType.Any;
    }

    /// <summary>
    ///     由实例推断光影筛选条件：OptiFine 优先，其次 Iris/Oculus，最后视为原版可用。
    /// </summary>
    private static string GetInstanceShaderLoaderTag(McInstance instance)
    {
        if (!instance.IsLoaded)
            instance.Load();
        if (instance.Info.HasOptiFine ||
            LocalComp.GetModLocalCompByKeywords(instance, "optifine", "optifine") is not null)
            return "/optifine";
        if (LocalComp.GetModLocalCompByKeywords(instance, "iris", "iris") is not null ||
            LocalComp.GetModLocalCompByKeywords(instance, "oculus", "oculus") is not null)
            return "/iris";
        return "/vanilla";
    }

    /// <summary>最近一次请求所用的按实例筛选目标，用于快速下载时跳过选择存放位置。</summary>
    private McInstance? _requestInstance;

    private Comp.CompProjectRequest LoaderInput()
    {
        var request = new Comp.CompProjectRequest(PageType, storage, (page + 1) * PageSize);
        var isInstanceMode = IsInstanceFilterMode;
        var filterInstance = isInstanceMode ? GetSelectedFilterInstance() : null;
        _requestInstance = filterInstance;
        if (filterInstance is { IsLoaded: false })
            filterInstance.Load();

        string? gameVersion;
        if (isInstanceMode)
        {
            gameVersion = filterInstance?.Info.VanillaName;
            if (gameVersion is not null && !(gameVersion.Contains('.') || gameVersion.Contains('w')))
                gameVersion = null;
        }
        else
        {
            gameVersion = TextSearchVersion.Text == Lang.Text("Download.Comp.Filter.Version.AllInputAvailable")
                ? null
                : TextSearchVersion.Text.Contains('.') || TextSearchVersion.Text.Contains('w')
                    ? TextSearchVersion.Text
                    : null;
        }

        request.searchText = PanSearchBox.Text.Trim();
        if (request.searchText.RegexCheck(@"\|.*[\u4e00-\u9fff]"))
            request.searchText = request.searchText.BeforeFirst("|").Trim();
        else if (request.searchText.RegexCheck(@"[\u4e00-\u9fff].*\|"))
            request.searchText = request.searchText.AfterFirst("|").Trim();
        request.gameVersion = gameVersion;
        var selectedTag = (ComboSearchTag.SelectedItem as FrameworkElement)?.Tag?.ToString();
        var loaderTag = isInstanceMode
            ? PageType == Comp.CompType.Shader && filterInstance is not null
                ? GetInstanceShaderLoaderTag(filterInstance)
                : null
            : (ComboSearchShaderLoader.SelectedItem as FrameworkElement)?.Tag?.ToString();

        request.tag = (PageType == Comp.CompType.Shader
            ? string.IsNullOrEmpty(loaderTag)
                ? selectedTag
                : selectedTag + loaderTag
            : selectedTag) ?? string.Empty;
        request.modLoader = PageType is Comp.CompType.Mod or Comp.CompType.ModPack
            ? isInstanceMode
                ? filterInstance is null ? Comp.CompLoaderType.Any : GetInstanceModLoader(filterInstance)
                : (Comp.CompLoaderType)Common.Val(((MyComboBoxItem)ComboSearchLoader.SelectedItem).Tag)
            : Comp.CompLoaderType.Any;
        request.source = (Comp.CompSourceType)Common.Val(((MyComboBoxItem)ComboSearchSource.SelectedItem).Tag);
        request.sort = (Comp.CompSortType)Common.Val(((MyComboBoxItem)ComboSearchSort.SelectedItem).Tag);
        return request;
    }

    #endregion

    #region 搜索

    // 搜索按钮
    private void StartNewSearch()
    {
        page = 0;
        object? argInput = LoaderInput();
        if (loader.ShouldStart(ref argInput))
            storage = new Comp.CompProjectStorage(); // 避免连续搜索两次使得 CompProjectStorage 引用丢失（#1311）
        loader.Start();
    }

    private void EnterTrigger(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            StartNewSearch();
    }

    private void ResetFilter()
    {
        PanSearchBox.Text = "";
        ComboSearchVersionMode.SelectedIndex = GetDefaultFilterMode();
        TextSearchVersion.Text = Lang.Text("Download.Comp.Filter.Version.AllInputAvailable");
        TextSearchVersion.SelectedIndex = 0;
        ComboSearchSource.SelectedIndex = 0;
        ComboSearchTag.SelectedIndex = 0;
        ComboSearchLoader.SelectedIndex = 0;
        ComboSearchShaderLoader.SelectedIndex = 0;
        ComboSearchSort.SelectedIndex = 0;
        SelectCurrentFilterInstance();
        loader.lastFinishedTime = 0L;
    }

    private void BtnSearchReset_Click(object sender, EventArgs e)
    {
        ResetFilter();
    }

    #endregion
}