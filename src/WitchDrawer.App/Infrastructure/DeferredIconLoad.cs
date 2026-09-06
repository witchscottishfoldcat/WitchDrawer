using System.Windows;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Infrastructure;

public static class DeferredIconLoad
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DeferredIconLoad),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if ((bool)eventArgs.NewValue)
        {
            element.Loaded += OnElementLoaded;
            element.DataContextChanged += OnElementDataContextChanged;
            RequestIconIfVisible(element);
            return;
        }

        element.Loaded -= OnElementLoaded;
        element.DataContextChanged -= OnElementDataContextChanged;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs eventArgs)
    {
        RequestIconIfVisible((FrameworkElement)sender);
    }

    private static void OnElementDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        RequestIconIfVisible((FrameworkElement)sender);
    }

    private static void RequestIconIfVisible(FrameworkElement element)
    {
        if (element.IsLoaded && element.DataContext is DrawerItemViewModel item)
        {
            item.EnsureIconLoaded();
        }
    }
}
