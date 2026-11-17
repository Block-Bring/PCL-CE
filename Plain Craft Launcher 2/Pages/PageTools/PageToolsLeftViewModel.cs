using PCL.Core.App;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>工具页面左侧导航栏的视图模型。</summary>
public class PageToolsLeftViewModel : ViewModelBase
{
    public bool IsHiddenPage(bool isGameLinkChecked, bool isTestChecked, bool isForceShow)
    {
        var hide = Config.Preference.Hide;
        var isHiddenPage = isGameLinkChecked && hide.ToolsGameLink || isTestChecked && hide.ToolsTest;
        return isForceShow ? false : isHiddenPage;
    }

    public FormMain.PageSubType ResolveInitialPageId(bool isForceShow = false)
    {
        var hide = Config.Preference.Hide;
        if (isForceShow || !hide.ToolsGameLink) return FormMain.PageSubType.ToolsGameLink;
        return FormMain.PageSubType.ToolsTest;
    }

    /// <summary>
    ///     将指向已隐藏页面的导航重定向到可用页面，避免构造联机页时初始化本地监听。
    /// </summary>
    public FormMain.PageSubType ResolveVisiblePageId(FormMain.PageSubType id, bool isForceShow)
    {
        if (isForceShow) return id;
        var hide = Config.Preference.Hide;
        if (id == FormMain.PageSubType.ToolsGameLink && hide.ToolsGameLink)
            return FormMain.PageSubType.ToolsTest;
        return id;
    }
}
