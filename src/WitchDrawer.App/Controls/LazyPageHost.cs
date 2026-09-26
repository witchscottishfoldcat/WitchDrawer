using System.Windows;
using System.Windows.Controls;

namespace WitchDrawer.App.Controls;

/// <summary>
/// 延迟页面宿主：承载启动时不显示的整页视图（关于页、归档页等）。
/// 页面控件在主窗口 InitializeComponent 时就会构建完整可视化树，
/// 即使处于 Collapsed 也占用启动时间；这里改为首次可见时才实例化，
/// 之后缓存复用同一实例，切换页面不再重建。
/// </summary>
public sealed class LazyPageHost : ContentControl
{
    public static readonly DependencyProperty PageTypeProperty = DependencyProperty.Register(
        nameof(PageType),
        typeof(Type),
        typeof(LazyPageHost),
        new PropertyMetadata(null));

    public Type? PageType
    {
        get => (Type?)GetValue(PageTypeProperty);
        set => SetValue(PageTypeProperty, value);
    }

    public LazyPageHost()
    {
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && Content is null && PageType is not null)
        {
            Content = Activator.CreateInstance(PageType);
        }
    }
}
