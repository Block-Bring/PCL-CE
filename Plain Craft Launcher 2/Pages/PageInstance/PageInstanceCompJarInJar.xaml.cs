using PCL.Core.App.Localization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CompFile = PCL.LocalComp.LocalCompFile;

namespace PCL;

public partial class PageInstanceCompJarInJar : IPageView
{
    private readonly PageInstanceCompJarInJarViewModel _vm = new();
    private readonly Stopwatch _refreshClock = new();
    private JarInJarIndex _index = null!;
    private string? _instanceMc;
    private string _search = "";
    private bool _refreshed;
    private int _refreshVersion;

    // 单次刷新内的计算缓存：刷新时整体清空，避免同一 mod 被重复解析
    private readonly Dictionary<CompFile, string> _displayNames = new();
    private readonly Dictionary<CompFile, List<JarInJarIndex.DepRow>> _ownDependencies = new();
    private readonly Dictionary<(CompFile Mod, JarInJarIndex.DepRow Dep), JijDepStatus> _depStatuses = new();
    private readonly Dictionary<string, DataTemplate> _templates = new();

    public PageInstanceCompJarInJar()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => RefreshOnNavigate();
        Unloaded += (_, _) => _refreshed = false;
    }

    private void BtnBack_Click(object sender, MouseButtonEventArgs e) => GoBack();

    private void BtnEmptyBack_Click(object sender, MouseButtonEventArgs e) => GoBack();

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e) => _ = RefreshListAsync(true);

    private void SearchBox_TextChanged(object sender, EventArgs e)
    {
        _search = (SearchBox.Text).Trim();
        _ = RefreshListAsync(false);
    }

    public PageInstanceCompJarInJarViewModel ViewModel => _vm;

    public void OnNavigatedTo(object? parameter)
    {
        RefreshOnNavigate();
    }

    public void OnNavigatedFrom()
    {
    }

    private void RefreshOnNavigate()
    {
        if (_refreshed)
            return;
        _refreshed = true;
        _ = RefreshListAsync(true);
    }

    private bool _Match(CompFile m)
    {
        if (_search.Length == 0) return true;
        return _Contains(_DisplayName(m)) || _Contains(m.ModId) || _Contains(m.FileName);
    }

    private bool _Contains(string? s) =>
        !string.IsNullOrEmpty(s) && s.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

    private bool _AnyEmbeddedMatch(List<CompFile>? embedded) =>
        embedded is not null && embedded.Any(e => _Match(e) || _AnyEmbeddedMatch(e.EmbeddedMods));

    private static void GoBack()
        => Launcher.frmInstanceLeft?.PageChange(FormMain.PageSubType.VersionMod);

    /// <summary>后台内嵌解析完成后刷新当前页。</summary>
    public void OnJijResolved()
    {
        if (IsVisible) _ = RefreshListAsync(false);
    }

    #region 列表构建

    private async Task RefreshListAsync(bool showLoading)
    {
        var version = ++_refreshVersion;
        if (showLoading) _vm.ShowLoading();
        _refreshClock.Restart();
        _displayNames.Clear();
        _ownDependencies.Clear();
        _depStatuses.Clear();

        try
        {
            // 先完成一次布局并等到空闲，确保加载卡片真的绘制出来、动画跑起来，再开始构建
            UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (version != _refreshVersion) return;

            var output = LocalComp.CompResourceListLoader.output!;
            if (!_OutputBelongsToCurrentInstance(output))
            {
                _vm.HideLoading();
                GoBack();
                return;
            }

            var allMods = output
                .Where(m => !m.IsFolder && m.State != CompFile.LocalFileStatus.Unavailable)
                .ToList();
            _instanceMc = PageInstanceLeft.McInstance.Info.VanillaName;
            _index = new JarInJarIndex(allMods, _instanceMc);
            if (!await YieldIfDueAsync(version)) return;

            _vm.WarningRows.Clear();
            foreach (var host in allMods)
            {
                if (host.State != CompFile.LocalFileStatus.Fine) continue;
                if (_Match(host)) _AppendMissingWarning(host, null, host.DetectedLoader);
                foreach (var node in _index.GetLoadableEmbedded(host))
                    if (_Match(node))
                        _AppendMissingWarning(node, host, node.JijLoader);
                if (!await YieldIfDueAsync(version)) return;
            }

            // 标记为支持库的模组若没有任何其他模组依赖它，提示可能是多余的
            foreach (var mod in allMods)
            {
                if (mod.State != CompFile.LocalFileStatus.Fine || !_IsLibrary(mod)) continue;
                if (!_Match(mod) || _index.IsDependedOnByOthers(mod)) continue;

                var text = Lang.Text("Instance.Resource.Mod.JarInJar.Warning.UnusedLibrary", _DisplayName(mod));
                var otherTags = Comp.GetLocalizedTags(mod.Comp?.Tags.Where(tag =>
                        !string.Equals(tag, _LibraryTagKey, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (otherTags.Count > 0)
                    text += Lang.Text("Instance.Resource.Mod.JarInJar.Warning.UnusedLibrary.OtherTags",
                        string.Join(Lang.Text("Common.Format.ListSeparator"), otherTags));

                _vm.WarningRows.Add(new JijNoticeRow
                {
                    Text = text,
                    Foreground = BrushWarn
                });
                if (!await YieldIfDueAsync(version)) return;
            }

            var hasWarning = _vm.WarningRows.Count > 0;

            _vm.ConflictRows.Clear();
            var conflictCount = 0;
            foreach (var (a, b, hard) in _index.FindActiveConflicts())
            {
                if (!_Match(a) && !_Match(b)) continue;
                _vm.ConflictRows.Add(_MakeConflictRow(a, b, hard));
                conflictCount++;
                if (!await YieldIfDueAsync(version)) return;
            }

            PanRelationList.Children.Clear();
            var relationCount = 0;
            foreach (var host in allMods)
            {
                var hostDeps = _GetOwnDependencies(host, host.DetectedLoader)
                    .Where(d => !DependencyIds.IsPlatform(d.DepId)).ToList();
                if (hostDeps.Count > 0 && (_Match(host) || hostDeps.Any(d => _Contains(d.DepId))))
                {
                    PanRelationList.Children.Add(_MakeRelationCard(host, null, hostDeps));
                    relationCount++;
                }

                foreach (var node in _index.GetLoadableEmbedded(host))
                {
                    var nodeDeps = _GetOwnDependencies(node, node.JijLoader)
                        .Where(d => !DependencyIds.IsPlatform(d.DepId)).ToList();
                    if (nodeDeps.Count == 0) continue;
                    if (!_Match(node) && !nodeDeps.Any(d => _Contains(d.DepId))) continue;
                    PanRelationList.Children.Add(_MakeRelationCard(node, host, nodeDeps));
                    relationCount++;
                }

                if (!await YieldIfDueAsync(version)) return;
            }

            PanBundledList.Children.Clear();
            var bundledMods = allMods
                .Where(m => m.EmbeddedMods is { Count: > 0 } && (_Match(m) || _AnyEmbeddedMatch(m.EmbeddedMods)))
                .ToList();
            foreach (var mod in bundledMods)
            {
                PanBundledList.Children.Add(_MakeBundledCard(mod));
                if (!await YieldIfDueAsync(version)) return;
            }

            // 搜索无结果时不显示"没有模组"空态（避免误导为实例本身没有），仅留空白
            _vm.ApplySectionVisibility(hasWarning, conflictCount, relationCount, bundledMods.Count,
                _search.Length > 0);
            _vm.HideLoading();
        }
        catch (Exception ex)
        {
            Common.Log(ex, "刷新模组关系页失败");
            if (version == _refreshVersion) _vm.HideLoading();
        }
    }

    /// <summary>构建耗时较长时按帧让出 UI 线程，保证加载动画持续绘制。返回 false 表示本次刷新已被新刷新取代。</summary>
    private async Task<bool> YieldIfDueAsync(int version)
    {
        if (version != _refreshVersion) return false;
        if (_refreshClock.ElapsedMilliseconds < 16) return true;
        _refreshClock.Restart();
        await Dispatcher.Yield(DispatcherPriority.Background);
        return version == _refreshVersion;
    }

    private MyCard _MakeCard(string title, IEnumerable<object> rows, string templateKey)
    {
        var card = new MyCard
        { Title = title, CanSwap = true, Margin = new Thickness(0, 0, 0, 10) };
        // 创建时就折叠内容：否则首次布局会在 Loaded 折叠前把全部行实例化一遍
        var content = new ItemsControl
        {
            Margin = new Thickness(20, MyCard.SwapedHeight, 18, 30),
            VerticalAlignment = VerticalAlignment.Top,
            ItemsSource = rows,
            ItemTemplate = _GetTemplate(templateKey),
            Visibility = Visibility.Collapsed
        };
        card.Children.Add(content);
        card.SwapControl = content;
        card.IsSwapped = true;
        return card;
    }

    private void _AppendMissingWarning(CompFile mod, CompFile? parent, string? loader)
    {
        var probed = _GetOwnDependencies(mod, loader)
            .Where(d => !DependencyIds.IsPlatform(d.DepId) && !d.Optional)
            .Select(d => (d, status: _Analyze(mod, d)))
            .ToList();
        var missing = probed.Where(x => x.status == JijDepStatus.Missing).Select(x => x.d.DepId).ToList();
        var mismatch = probed.Where(x => x.status == JijDepStatus.VersionMismatch)
            .Select(x => Lang.Text("Instance.Resource.Mod.JarInJar.Warning.VersionMismatch.Detail",
                x.d.DepId, x.d.Raw ?? "*", string.Join(", ", _index.GetProviderVersions(mod, x.d.DepId))))
            .ToList();
        // 版本匹配的 provider 全部被禁用：运行时同样不可用，需提示（用户重新启用即可修复）
        var disabled = probed.Where(x => x.status == JijDepStatus.Disabled).Select(x => x.d.DepId).ToList();
        if (missing.Count == 0 && mismatch.Count == 0 && disabled.Count == 0) return;
        var name = parent is null
            ? _DisplayName(mod)
            : _DisplayName(mod) + " " +
              Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.From", _DisplayName(parent));
        if (missing.Count > 0)
            _vm.WarningRows.Add(new JijNoticeRow
            {
                Text = Lang.Text("Instance.Resource.Mod.JarInJar.Warning.Missing", name, string.Join(", ", missing)),
                Foreground = BrushError
            });
        if (mismatch.Count > 0)
            _vm.WarningRows.Add(new JijNoticeRow
            {
                Text = Lang.Text("Instance.Resource.Mod.JarInJar.Warning.VersionMismatch", name,
                    string.Join(", ", mismatch)),
                Foreground = BrushWarn
            });
        if (disabled.Count > 0)
            _vm.WarningRows.Add(new JijNoticeRow
            {
                Text = Lang.Text("Instance.Resource.Mod.JarInJar.Warning.Disabled", name, string.Join(", ", disabled)),
                Foreground = BrushWarn
            });
    }

    private JijConflictRow _MakeConflictRow(CompFile a, CompFile b, bool hard) => new()
    {
        Severity = Lang.Text(hard
            ? "Instance.Resource.Mod.JarInJar.Conflict.Incompatible"
            : "Instance.Resource.Mod.JarInJar.Conflict.Discouraged"),
        SeverityBrush = hard ? BrushError : BrushWarn,
        Pair = "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Conflict.Pair", _DisplayName(a), _DisplayName(b))
    };

    private MyCard _MakeRelationCard(CompFile mod, CompFile? parent, List<JarInJarIndex.DepRow> deps)
    {
        var title = parent is null
            ? _DisplayName(mod)
            : _DisplayName(mod) + "  " +
              Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.From", _DisplayName(parent));
        return _MakeCard(title, deps.Select(dep => BuildDependencyRow(mod, dep)).ToList(), "JijDependencyRowTemplate");
    }

    private MyCard _MakeBundledCard(CompFile mod)
        => _MakeCard(_DisplayName(mod), _BuildBundledRows(mod), "JijTreeRowTemplate");

    private List<JijTreeRow> _BuildBundledRows(CompFile mod)
    {
        var selected = mod.State == CompFile.LocalFileStatus.Fine
            ? _index.GetLoadableEmbedded(mod).ToHashSet()
            : new HashSet<CompFile>();
        var rows = new List<JijTreeRow>();
        AppendTreeRows(rows, mod.EmbeddedMods, 0, selected);
        return rows;
    }

    private void AppendTreeRows(List<JijTreeRow> rows, List<CompFile> embedded, int depth, HashSet<CompFile> selected)
    {
        if (embedded is { Count: 0 }) return;

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
                .Select(v => (v, kind: McConstraintMatcher.Match(v.FileName, v.Version, v.JijTargetMcVersion,
                    v.JijLoader, _instanceMc)))
                .OrderByDescending(x => (int)x.kind).ToList();
            var selectedRep = versions.FirstOrDefault(selected.Contains);
            var rep = selectedRep ?? scored[0].v;
            var kind = selectedRep is null
                ? scored[0].kind
                : McConstraintMatcher.Match(rep.FileName, rep.Version, rep.JijTargetMcVersion, rep.JijLoader,
                    _instanceMc);
            var tooltip = string.Join("\n",
                versions.Select(v => string.IsNullOrWhiteSpace(v.Version) ? v.FileName : v.Version));
            rows.Add(BuildTreeRow(rep, depth, versions.Count, kind, tooltip, selectedRep is not null));
            AppendTreeRows(rows, rep.EmbeddedMods, depth + 1, selected);
        }

        foreach (var e in ungrouped)
        {
            rows.Add(BuildTreeRow(e, depth, 0, McConstraintMatcher.MatchKind.None));
            AppendTreeRows(rows, e.EmbeddedMods, depth + 1, selected);
        }
    }

    private JijTreeRow BuildTreeRow(CompFile e, int depth, int versionCount, McConstraintMatcher.MatchKind kind,
        string? versionsTooltip = null, bool selected = false)
    {
        var ver = _CleanPlaceholder(e.Version);
        var mc = _CleanPlaceholder(e.JijTargetMcVersion);
        var showVersions = versionCount > 1;
        var isCurrent = selected || kind is McConstraintMatcher.MatchKind.Exact or McConstraintMatcher.MatchKind.Range;
        var isIncompatible = !isCurrent && kind == McConstraintMatcher.MatchKind.Incompatible;
        return new JijTreeRow
        {
            Margin = new Thickness(8 + depth * 20, 2, 0, 2),
            Name = "• " + _DisplayName(e),
            Version = ver is null ? "" : "  (" + ver + ")",
            Loader = string.IsNullOrEmpty(e.JijLoader) ? "" : "  [" + e.JijLoader + "]",
            McVersion = mc is null ? "" : "  (MC " + mc + ")",
            Versions = showVersions
                ? "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.Versions", versionCount)
                : "",
            VersionsTooltip = showVersions && !string.IsNullOrEmpty(versionsTooltip) ? versionsTooltip : null,
            Status = isCurrent
                ? "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.Current")
                : isIncompatible
                    ? "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Bundled.Incompatible")
                    : "",
            StatusBrush = isCurrent ? BrushOk : isIncompatible ? BrushWarn : null
        };
    }

    private JijDependencyRow BuildDependencyRow(CompFile mod, JarInJarIndex.DepRow dep)
    {
        var status = _Analyze(mod, dep);
        var optional = dep.Optional;
        var text = dep.Raw is null ? dep.DepId : dep.DepId + " " + dep.Raw;
        var label = "  (" + _StatusLabel(status) + ")";
        // 可选依赖缺失不算问题，用主题灰；其余：内嵌提供灰、已装/已禁/缺失为绿/橙/红语义色
        var useThemeBrush = optional || status == JijDepStatus.Bundled;
        return new JijDependencyRow
        {
            Primary = "• " + text,
            Optional = optional ? "  " + Lang.Text("Instance.Resource.Mod.JarInJar.Dep.Optional") : "",
            GrayLabel = useThemeBrush ? label : "",
            ColorLabel = useThemeBrush ? "" : label,
            LabelBrush = useThemeBrush ? null : _StatusBrush(status)
        };
    }

    private static string _StatusLabel(JijDepStatus status) => Lang.Text(status switch
    {
        JijDepStatus.Installed => "Instance.Resource.Mod.JarInJar.Dep.Installed",
        JijDepStatus.Disabled => "Instance.Resource.Mod.JarInJar.Dep.Disabled",
        JijDepStatus.Bundled => "Instance.Resource.Mod.JarInJar.Dep.Bundled",
        JijDepStatus.VersionMismatch => "Instance.Resource.Mod.JarInJar.Dep.VersionMismatch",
        _ => "Instance.Resource.Mod.JarInJar.Dep.Missing"
    });

    private static Brush _StatusBrush(JijDepStatus status) => status switch
    {
        JijDepStatus.Installed => BrushOk,
        JijDepStatus.Disabled => BrushWarn,
        JijDepStatus.VersionMismatch => BrushWarn,
        _ => BrushError
    };

    #endregion

    #region 辅助

    // output 是否属于当前实例（mod 路径必在实例游戏目录 PathIndie 下）；空列表无从判断按匹配处理避免误弹
    private static bool _OutputBelongsToCurrentInstance(List<CompFile> output)
    {
        var inst = PageInstanceLeft.McInstance;
        if (output is { Count: 0 }) return false;
        try
        {
            var input = LocalComp.CompResourceListLoader.input;
            if (input is null || input.compType != Comp.CompType.Mod ||
                !string.Equals(Path.GetFullPath(input.gameVersion.PathInstance)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(inst.PathInstance)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return false;
            var first = output.FirstOrDefault(m => !string.IsNullOrEmpty(m.path));
            if (first is null) return true;
            var root = Path.GetFullPath(inst.PathIndie)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
            return Path.GetFullPath(first.path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // 未替换的版本占位符不显示。
    private static string? _CleanPlaceholder(string? v) => string.IsNullOrEmpty(v) || v.Contains("${") ? null : v;

    private const string _LibraryTagKey = "Download.Comp.Category.Library";

    private static bool _IsLibrary(CompFile mod) =>
        mod.Comp?.Tags.Contains(_LibraryTagKey, StringComparer.OrdinalIgnoreCase) == true;

    private string _DisplayName(CompFile m)
    {
        if (_displayNames.TryGetValue(m, out var cached)) return cached;

        string name;
        if (m.Comp is not null)
        {
            var t = m.Comp.GetControlTitle(false);
            name = t.Key + t.Value;
        }
        else
        {
            name = string.IsNullOrWhiteSpace(m.Name) ? m.FileName : m.Name;
        }

        _displayNames[m] = name;
        return name;
    }

    private List<JarInJarIndex.DepRow> _GetOwnDependencies(CompFile mod, string? loader)
    {
        if (_ownDependencies.TryGetValue(mod, out var deps)) return deps;
        deps = JarInJarIndex.BuildOwnDependencies(mod, loader ?? "");
        _ownDependencies[mod] = deps;
        return deps;
    }

    private JijDepStatus _Analyze(CompFile mod, JarInJarIndex.DepRow dep)
    {
        if (_depStatuses.TryGetValue((mod, dep), out var status)) return status;
        status = _index.Analyze(mod, dep);
        _depStatuses[(mod, dep)] = status;
        return status;
    }

    private DataTemplate _GetTemplate(string key)
    {
        if (!_templates.TryGetValue(key, out var template))
            _templates[key] = template = (DataTemplate)FindResource(key);
        return template;
    }

    private static readonly Brush BrushOk = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush BrushWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly Brush BrushError = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    #endregion
}
