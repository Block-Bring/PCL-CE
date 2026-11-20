using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PCL.Core.App.Localization;
using PCL.Navigation;
using PCL.UI.Controls;
using CompFile = PCL.LocalComp.LocalCompFile;

namespace PCL.Views.Pages.Instance;

/// <summary>
/// 模组关系页：按当前实例分析模组间的依赖、冲突与内嵌（Jar-in-Jar）结构， 并给出缺失前置、版本不符与被禁用的支持库提示。
/// </summary>
public partial class PageInstanceCompJarInJar : MyPageRight, ICompLocalPage
{
    private const string LibraryTagKey = "Download.Comp.Category.Library";

    private static readonly IBrush BrushOk = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush BrushWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly IBrush BrushError = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    private readonly Stopwatch _refreshClock = new();

    // 单次刷新内的计算缓存：刷新时整体清空，避免同一 mod 被重复解析
    private readonly Dictionary<CompFile, string> _displayNames = new();
    private readonly Dictionary<CompFile, List<JarInJarIndex.DepRow>> _ownDependencies = new();
    private readonly Dictionary<(CompFile Mod, JarInJarIndex.DepRow Dep), JijDepStatus> _depStatuses = new();

    private JarInJarIndex _index = null!;
    private string _instanceMc = "";
    private bool _refreshed;
    private int _refreshVersion;
    private string _search = "";

    public PageInstanceCompJarInJar()
    {
        InitializeComponent();
        DataContext = ViewModel;
        PanScroll = PanBack;

        BtnBack.Click += (_, _) => GoBack();
        BtnEmptyBack.Click += (_, _) => GoBack();
        BtnRefresh.Click += (_, _) => _ = RefreshListAsync(true);
        SearchBox.TextChanged += (_, _) =>
        {
            _search = (SearchBox.Text ?? "").Trim();
            _ = RefreshListAsync(false);
        };
    }

    public PageInstanceCompJarInJarViewModel ViewModel { get; } = new();

    public override void OnNavigatedTo(PageParameter? parameter)
    {
        if (_refreshed)
            return;
        _refreshed = true;
        PanBack.ScrollToHome();
        _ = RefreshListAsync(true);
    }

    public override void OnNavigatedFrom()
    {
        _refreshed = false;
    }

    private static void GoBack()
    {
        PageRouter.Navigate(
            PageIds.InstanceSetup,
            SubPageIds.VersionMod,
            PageInstanceLeft.ResolveInstance());
    }

    #region 分析刷新

    private async Task RefreshListAsync(bool showLoading)
    {
        var version = ++_refreshVersion;
        if (showLoading)
            ViewModel.ShowLoading();
        _refreshClock.Restart();
        _displayNames.Clear();
        _ownDependencies.Clear();
        _depStatuses.Clear();

        try
        {
            // 先完成一次布局并等到空闲，确保加载卡片真的绘制出来、动画跑起来，再开始构建
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (version != _refreshVersion)
                return;

            var output = LocalComp.CompResourceListLoader.output;
            if (output is null)
            {
                ViewModel.HideLoading();
                return;
            }

            if (!OutputBelongsToCurrentInstance(output))
            {
                ViewModel.HideLoading();
                GoBack();
                return;
            }

            var allMods = output
                .Where(m => !m.IsFolder && m.State != CompFile.LocalFileStatus.Unavailable)
                .ToList();
            _instanceMc = PageInstanceLeft.ResolveInstance()?.Info.VanillaName ?? "";
            _index = new JarInJarIndex(allMods, _instanceMc);
            if (!await YieldIfDueAsync(version))
                return;

            PanWarnList.Children.Clear();
            foreach (var host in allMods)
            {
                if (host.State != CompFile.LocalFileStatus.Fine)
                    continue;
                if (Match(host))
                    AppendMissingWarning(host, null, host.DetectedLoader);
                foreach (var node in _index.GetLoadableEmbedded(host))
                {
                    if (Match(node))
                        AppendMissingWarning(node, host, node.JijLoader);
                }

                if (!await YieldIfDueAsync(version))
                    return;
            }

            // 标记为支持库的模组若没有任何其他模组依赖它，提示可能是多余的
            foreach (var mod in allMods)
            {
                if (mod.State != CompFile.LocalFileStatus.Fine || !IsLibrary(mod))
                    continue;
                if (!Match(mod) || _index.IsDependedOnByOthers(mod))
                    continue;

                var text = Lang.Text(
                    "Instance.Resource.Mod.JarInJar.Warning.UnusedLibrary",
                    DisplayName(mod));
                var otherTags = Comp.GetLocalizedTags(mod.Comp?.Tags.Where(tag =>
                        !string.Equals(tag, LibraryTagKey, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (otherTags.Count > 0)
                {
                    text += Lang.Text(
                        "Instance.Resource.Mod.JarInJar.Warning.UnusedLibrary.OtherTags",
                        string.Join(Lang.Text("Common.Format.ListSeparator"), otherTags));
                }

                AddWarningRow(text, BrushWarn);
                if (!await YieldIfDueAsync(version))
                    return;
            }

            var hasWarning = PanWarnList.Children.Count > 0;

            PanConflictList.Children.Clear();
            var conflictCount = 0;
            foreach (var (a, b, hard) in _index.FindActiveConflicts())
            {
                if (!Match(a) && !Match(b))
                    continue;
                PanConflictList.Children.Add(MakeConflictRow(a, b, hard));
                conflictCount++;
                if (!await YieldIfDueAsync(version))
                    return;
            }

            PanRelationList.Children.Clear();
            var relationCount = 0;
            foreach (var host in allMods)
            {
                var hostDeps = GetOwnDependencies(host, host.DetectedLoader)
                    .Where(d => !DependencyIds.IsPlatform(d.DepId))
                    .ToList();
                if (hostDeps.Count > 0 && (Match(host) || hostDeps.Any(d => Contains(d.DepId))))
                {
                    PanRelationList.Children.Add(MakeRelationCard(host, null, hostDeps));
                    relationCount++;
                }

                foreach (var node in _index.GetLoadableEmbedded(host))
                {
                    var nodeDeps = GetOwnDependencies(node, node.JijLoader)
                        .Where(d => !DependencyIds.IsPlatform(d.DepId))
                        .ToList();
                    if (nodeDeps.Count == 0)
                        continue;
                    if (!Match(node) && !nodeDeps.Any(d => Contains(d.DepId)))
                        continue;
                    PanRelationList.Children.Add(MakeRelationCard(node, host, nodeDeps));
                    relationCount++;
                }

                if (!await YieldIfDueAsync(version))
                    return;
            }

            PanBundledList.Children.Clear();
            var bundledMods = allMods
                .Where(m => m.EmbeddedMods is { Count: > 0 }
                            && (Match(m) || AnyEmbeddedMatch(m.EmbeddedMods)))
                .ToList();
            foreach (var mod in bundledMods)
            {
                PanBundledList.Children.Add(MakeBundledCard(mod));
                if (!await YieldIfDueAsync(version))
                    return;
            }

            // 搜索无结果时不显示“没有模组”空态（避免误导为实例本身没有），仅留空白
            ViewModel.ApplySectionVisibility(
                hasWarning,
                conflictCount,
                relationCount,
                bundledMods.Count,
                _search.Length > 0);
            ViewModel.HideLoading();
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "刷新模组关系页失败");
            if (version == _refreshVersion)
                ViewModel.HideLoading();
        }
    }

    /// <summary>构建耗时较长时按帧让出 UI 线程，保证加载动画持续绘制。返回 false 表示本次刷新已被新刷新取代。</summary>
    private async Task<bool> YieldIfDueAsync(int version)
    {
        if (version != _refreshVersion)
            return false;
        if (_refreshClock.ElapsedMilliseconds < 16)
            return true;
        _refreshClock.Restart();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        return version == _refreshVersion;
    }

    #endregion

    #region 行与卡片构建

    private (MyCard Card, StackPanel Content) MakeCard(string title)
    {
        var content = new StackPanel
        {
            Margin = new Thickness(20, MyCard.SwapedHeight, 18, 30),
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false
        };
        var card = new MyCard
        {
            Title = title,
            CanSwap = true,
            Margin = new Thickness(0, 0, 0, 10),
            IsSwapped = true,
            SwapControl = content
        };
        card.Children.Add(content);
        return (card, content);
    }

    private void AddWarningRow(string text, IBrush foreground)
    {
        PanWarnList.Children.Add(
            new TextBlock
            {
                Text = text,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
    }

    private Border MakeConflictRow(CompFile a, CompFile b, bool hard)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 2)
        };
        panel.Children.Add(MakePart(
            Lang.Text(hard
                ? "Instance.Resource.Mod.JarInJar.Conflict.Incompatible"
                : "Instance.Resource.Mod.JarInJar.Conflict.Discouraged"),
            brush: hard ? BrushError : BrushWarn));
        panel.Children.Add(MakePart(
            "  " + Lang.Text(
                "Instance.Resource.Mod.JarInJar.Conflict.Pair",
                DisplayName(a),
                DisplayName(b)),
            "ColorBrush1"));
        return new Border { Child = panel };
    }

    private static TextBlock MakePart(string text, string? brushKey = null, IBrush? brush = null, bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal
        };
        if (brush is not null)
            block.Foreground = brush;
        else if (brushKey is not null)
            ContainerTheme.BindDynamic(block, TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    private void AppendMissingWarning(CompFile mod, CompFile? parent, string? loader)
    {
        var probed = GetOwnDependencies(mod, loader)
            .Where(d => !DependencyIds.IsPlatform(d.DepId) && !d.Optional)
            .Select(d => (d, status: Analyze(mod, d)))
            .ToList();
        var missing = probed.Where(x => x.status == JijDepStatus.Missing)
            .Select(x => x.d.DepId ?? "")
            .ToList();
        var mismatch = probed.Where(x => x.status == JijDepStatus.VersionMismatch)
            .Select(x => Lang.Text(
                "Instance.Resource.Mod.JarInJar.Warning.VersionMismatch.Detail",
                x.d.DepId ?? "",
                x.d.Raw ?? "*",
                string.Join(", ", _index.GetProviderVersions(mod, x.d.DepId ?? ""))))
            .ToList();
        // 版本匹配的 provider 全部被禁用：运行时同样不可用，需提示（用户重新启用即可修复）
        var disabled = probed.Where(x => x.status == JijDepStatus.Disabled)
            .Select(x => x.d.DepId ?? "")
            .ToList();
        if (missing.Count == 0 && mismatch.Count == 0 && disabled.Count == 0)
            return;
        var name = parent is null
            ? DisplayName(mod)
            : DisplayName(mod) + " "
              + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.From", DisplayName(parent));
        if (missing.Count > 0)
        {
            AddWarningRow(
                Lang.Text(
                    "Instance.Resource.Mod.JarInJar.Warning.Missing",
                    name,
                    string.Join(", ", missing)),
                BrushError);
        }

        if (mismatch.Count > 0)
        {
            AddWarningRow(
                Lang.Text(
                    "Instance.Resource.Mod.JarInJar.Warning.VersionMismatch",
                    name,
                    string.Join(", ", mismatch)),
                BrushWarn);
        }

        if (disabled.Count > 0)
        {
            AddWarningRow(
                Lang.Text(
                    "Instance.Resource.Mod.JarInJar.Warning.Disabled",
                    name,
                    string.Join(", ", disabled)),
                BrushWarn);
        }
    }

    private MyCard MakeRelationCard(CompFile mod, CompFile? parent, List<JarInJarIndex.DepRow> deps)
    {
        var title = parent is null
            ? DisplayName(mod)
            : DisplayName(mod) + "  "
              + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.From", DisplayName(parent));
        var (card, content) = MakeCard(title);
        foreach (var dep in deps)
            content.Children.Add(MakeDependencyRow(mod, dep));
        return card;
    }

    private Border MakeDependencyRow(CompFile mod, JarInJarIndex.DepRow dep)
    {
        var status = Analyze(mod, dep);
        var optional = dep.Optional;
        var text = dep.Raw is null ? dep.DepId ?? "" : (dep.DepId ?? "") + " " + dep.Raw;
        var label = "  (" + StatusLabel(status) + ")";
        // 可选依赖缺失不算问题，用主题灰；其余：内嵌提供灰、已装/已禁/缺失为绿/橙/红语义色
        var useThemeBrush = optional || status == JijDepStatus.Bundled;
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 2, 0, 2)
        };
        panel.Children.Add(MakePart("• " + text, "ColorBrush1"));
        if (optional)
            panel.Children.Add(MakePart("  " + Lang.Text("Instance.Resource.Mod.JarInJar.Dep.Optional"), "ColorBrush2"));
        if (useThemeBrush)
            panel.Children.Add(MakePart(label, "ColorBrush2"));
        else
            panel.Children.Add(MakePart(label, brush: StatusBrush(status)));
        return new Border { Child = panel };
    }

    private MyCard MakeBundledCard(CompFile mod)
    {
        var (card, content) = MakeCard(DisplayName(mod));
        foreach (var row in BuildBundledRows(mod))
            content.Children.Add(row);
        return card;
    }

    private List<Border> BuildBundledRows(CompFile mod)
    {
        var selected = mod.State == CompFile.LocalFileStatus.Fine
            ? _index.GetLoadableEmbedded(mod).ToHashSet()
            : [];
        var rows = new List<Border>();
        AppendTreeRows(rows, mod.EmbeddedMods, 0, selected);
        return rows;
    }

    private void AppendTreeRows(
        List<Border> rows,
        List<CompFile>? embedded,
        int depth,
        HashSet<CompFile> selected)
    {
        if (embedded is not { Count: > 0 })
            return;

        var ungrouped = embedded.Where(e => string.IsNullOrEmpty(e.ModId)).ToList();
        foreach (var g in embedded.Where(e => !string.IsNullOrEmpty(e.ModId))
                     .GroupBy(e => e.ModId, StringComparer.OrdinalIgnoreCase))
        {
            var versions = g.ToList();
            if (versions.Count == 1)
            {
                rows.Add(BuildTreeRow(versions[0], depth, 0, McConstraintMatcher.MatchKind.None));
                AppendTreeRows(rows, versions[0].EmbeddedMods, depth + 1, selected);
                continue;
            }

            var scored = versions
                .Select(v => (v, kind: MatchKind(v)))
                .OrderByDescending(x => (int)x.kind)
                .ToList();
            var selectedRep = versions.FirstOrDefault(selected.Contains);
            var representative = selectedRep ?? scored[0].v;
            var kind = selectedRep is null ? scored[0].kind : MatchKind(selectedRep);
            var tooltip = string.Join(
                "\n",
                versions.Select(v => string.IsNullOrWhiteSpace(v.Version) ? v.FileName : v.Version));
            rows.Add(BuildTreeRow(
                representative,
                depth,
                versions.Count,
                kind,
                tooltip,
                selectedRep is not null));
            AppendTreeRows(rows, representative.EmbeddedMods, depth + 1, selected);
        }

        foreach (var e in ungrouped)
        {
            rows.Add(BuildTreeRow(e, depth, 0, McConstraintMatcher.MatchKind.None));
            AppendTreeRows(rows, e.EmbeddedMods, depth + 1, selected);
        }
    }

    private McConstraintMatcher.MatchKind MatchKind(CompFile embedded)
    {
        return McConstraintMatcher.Match(
            embedded.FileName,
            embedded.Version,
            embedded.JijTargetMcVersion ?? "",
            embedded.JijLoader ?? "",
            _instanceMc);
    }

    private Border BuildTreeRow(
        CompFile e,
        int depth,
        int versionCount,
        McConstraintMatcher.MatchKind kind,
        string? versionsTooltip = null,
        bool selected = false)
    {
        var ver = CleanPlaceholder(e.Version);
        var mc = CleanPlaceholder(e.JijTargetMcVersion);
        var showVersions = versionCount > 1;
        var isCurrent = selected
                        || kind is McConstraintMatcher.MatchKind.Exact
                            or McConstraintMatcher.MatchKind.Range;
        var isIncompatible = !isCurrent && kind == McConstraintMatcher.MatchKind.Incompatible;
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8 + depth * 20, 2, 0, 2)
        };
        panel.Children.Add(MakePart("• " + DisplayName(e), "ColorBrush1", bold: true));
        if (ver is not null)
            panel.Children.Add(MakePart("  (" + ver + ")", "ColorBrush2"));
        if (!string.IsNullOrEmpty(e.JijLoader))
            panel.Children.Add(MakePart("  [" + e.JijLoader + "]", "ColorBrush3"));
        if (mc is not null)
            panel.Children.Add(MakePart("  (MC " + mc + ")", "ColorBrushInfo"));
        if (showVersions)
        {
            var versionsBlock = MakePart(
                "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.Versions", versionCount),
                "ColorBrush2");
            if (!string.IsNullOrEmpty(versionsTooltip))
                ToolTip.SetTip(versionsBlock, versionsTooltip);
            panel.Children.Add(versionsBlock);
        }

        if (isCurrent)
            panel.Children.Add(MakePart("  " + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.Current"), brush: BrushOk));
        else if (isIncompatible)
            panel.Children.Add(MakePart(
                "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.Incompatible"),
                brush: BrushWarn));
        return new Border { Child = panel };
    }

    private static string StatusLabel(JijDepStatus status)
    {
        return Lang.Text(status switch
        {
            JijDepStatus.Installed => "Instance.Resource.Mod.JarInJar.Dep.Installed",
            JijDepStatus.Disabled => "Instance.Resource.Mod.JarInJar.Dep.Disabled",
            JijDepStatus.Bundled => "Instance.Resource.Mod.JarInJar.Dep.Bundled",
            JijDepStatus.VersionMismatch => "Instance.Resource.Mod.JarInJar.Dep.VersionMismatch",
            _ => "Instance.Resource.Mod.JarInJar.Dep.Missing"
        });
    }

    private static IBrush StatusBrush(JijDepStatus status)
    {
        return status switch
        {
            JijDepStatus.Installed => BrushOk,
            JijDepStatus.Disabled => BrushWarn,
            JijDepStatus.VersionMismatch => BrushWarn,
            _ => BrushError
        };
    }

    #endregion

    #region 辅助

    private bool Match(CompFile m)
    {
        if (_search.Length == 0)
            return true;
        return Contains(DisplayName(m)) || Contains(m.ModId) || Contains(m.FileName);
    }

    private bool Contains(string? s)
    {
        return !string.IsNullOrEmpty(s)
               && s.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool AnyEmbeddedMatch(List<CompFile>? embedded)
    {
        return embedded is not null
               && embedded.Any(e => Match(e) || AnyEmbeddedMatch(e.EmbeddedMods));
    }

    // output 是否属于当前实例（mod 路径必在实例游戏目录 PathIndie 下）；空列表无从判断按匹配处理避免误弹
    private static bool OutputBelongsToCurrentInstance(List<CompFile> output)
    {
        var instance = PageInstanceLeft.ResolveInstance();
        if (instance is null || output is { Count: 0 })
            return false;
        try
        {
            var input = LocalComp.CompResourceListLoader.input;
            if (input is null
                || input.compType != Comp.CompType.Mod
                || !string.Equals(
                    Path.GetFullPath(input.gameVersion.PathInstance)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(instance.PathInstance)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var first = output.FirstOrDefault(m => !string.IsNullOrEmpty(m.path));
            if (first is null)
                return true;
            var root = Path.GetFullPath(instance.PathIndie)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            return Path.GetFullPath(first.path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // 未替换的版本占位符不显示
    private static string? CleanPlaceholder(string? version)
    {
        return string.IsNullOrEmpty(version) || version.Contains("${") ? null : version;
    }

    private static bool IsLibrary(CompFile mod)
    {
        return mod.Comp?.Tags.Contains(LibraryTagKey, StringComparer.OrdinalIgnoreCase) == true;
    }

    private string DisplayName(CompFile mod)
    {
        if (_displayNames.TryGetValue(mod, out var cached))
            return cached;

        string name;
        if (mod.Comp is not null)
        {
            var title = mod.Comp.GetControlTitle(false);
            name = title.Key + title.Value;
        }
        else
        {
            name = string.IsNullOrWhiteSpace(mod.Name) ? mod.FileName : mod.Name;
        }

        _displayNames[mod] = name;
        return name;
    }

    private List<JarInJarIndex.DepRow> GetOwnDependencies(CompFile mod, string? loader)
    {
        if (_ownDependencies.TryGetValue(mod, out var deps))
            return deps;
        deps = JarInJarIndex.BuildOwnDependencies(mod, loader ?? "");
        _ownDependencies[mod] = deps;
        return deps;
    }

    private JijDepStatus Analyze(CompFile mod, JarInJarIndex.DepRow dep)
    {
        if (_depStatuses.TryGetValue((mod, dep), out var status))
            return status;
        status = _index.Analyze(mod, dep);
        _depStatuses[(mod, dep)] = status;
        return status;
    }

    #endregion

    #region ICompLocalPage

    public bool LoadProgressVisible
    {
        get => false;
        set { }
    }

    public string? CurrentFolderPath => null;

    public void SetLoadText(string key)
    {
    }

    public void UpdateOnly()
    {
    }

    public void RefreshModList()
    {
        _ = RefreshListAsync(false);
    }

    #endregion
}
