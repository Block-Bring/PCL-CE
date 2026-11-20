using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>模组关系页（依赖、冲突与内嵌结构）的视图模型。</summary>
public partial class PageInstanceCompJarInJarViewModel : ViewModelBase
{
    [ObservableProperty] public partial bool PanLoadVisible { get; set; } = true;

    [ObservableProperty] public partial bool CardWarningsVisible { get; set; }

    [ObservableProperty] public partial bool CardConflictsVisible { get; set; }

    [ObservableProperty] public partial bool SectionRelationsVisible { get; set; }

    [ObservableProperty] public partial bool SectionBundledVisible { get; set; }

    [ObservableProperty] public partial bool PanEmptyVisible { get; set; }

    public void ShowLoading()
    {
        PanLoadVisible = true;
    }

    public void HideLoading()
    {
        PanLoadVisible = false;
    }

    public void ApplySectionVisibility(
        bool hasWarning,
        int conflictCount,
        int relationCount,
        int bundledCount,
        bool hasSearch)
    {
        CardWarningsVisible = hasWarning;
        CardConflictsVisible = conflictCount > 0;
        SectionRelationsVisible = relationCount > 0;
        SectionBundledVisible = bundledCount > 0;
        PanEmptyVisible = !hasWarning
                          && conflictCount <= 0
                          && relationCount <= 0
                          && bundledCount <= 0
                          && !hasSearch;
    }
}
