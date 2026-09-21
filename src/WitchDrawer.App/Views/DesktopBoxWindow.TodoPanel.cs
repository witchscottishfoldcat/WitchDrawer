using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace WitchDrawer.App.Views;

/// <summary>
/// 待办面板：覆盖滚动条联动、标题输入激活与回车提交。
/// </summary>
public partial class DesktopBoxWindow
{
    private void OnTodoOverlayScroll(object sender, ScrollEventArgs e)
    {
        if (sender is not ScrollBar scrollBar)
        {
            return;
        }

        var listBox = FindVisualAncestor<ListBox>(scrollBar);
        var scrollViewer = listBox is null
            ? null
            : FindVisualChild<ScrollViewer>(listBox);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(e.NewValue);
        e.Handled = true;
    }

    private void OnTodoScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var listBox = FindVisualAncestor<ListBox>(scrollViewer);
        if (listBox?.Template.FindName("TodoOverlayScrollBar", listBox) is not ScrollBar scrollBar)
        {
            return;
        }

        var maximum = Math.Max(0, e.ExtentHeight - e.ViewportHeight);
        scrollBar.SetCurrentValue(RangeBase.MaximumProperty, maximum);
        scrollBar.SetCurrentValue(ScrollBar.ViewportSizeProperty, Math.Max(0, e.ViewportHeight));
        scrollBar.SetCurrentValue(
            RangeBase.ValueProperty,
            Math.Clamp(e.VerticalOffset, 0, maximum));

        if (Math.Abs(e.VerticalChange) > double.Epsilon)
        {
            RevealTodoScrollBarForScroll(scrollBar);
        }
    }

    private static void RevealTodoScrollBarForScroll(ScrollBar scrollBar)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(900),
            FillBehavior = FillBehavior.Stop,
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        scrollBar.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnTodoTitlePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 待办输入需要键盘焦点：盒子是 NOACTIVATE，点击本身不激活窗口，
        // 这里显式激活（用户明确要开始输入，盒子短暂到前面、点别处即收回）。
        Activate();
        TodoTitleTextBox.Focus();
        Keyboard.Focus(TodoTitleTextBox);
    }

    private async void OnTodoTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !ViewModel.AddTodoCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.AddTodoCommand.ExecuteAsync(null);
        TodoTitleTextBox.Focus();
    }
}
