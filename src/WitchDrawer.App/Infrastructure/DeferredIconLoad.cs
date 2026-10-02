using System.Windows;
using System.Windows.Media;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Infrastructure;

public static class DeferredIconLoad
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DeferredIconLoad),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty RequestStateProperty = DependencyProperty.RegisterAttached(
        "RequestState",
        typeof(RequestState),
        typeof(DeferredIconLoad),
        new PropertyMetadata(null));

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
            var state = new RequestState(element);
            element.SetValue(RequestStateProperty, state);
            state.Attach();
        }
        else
        {
            (element.GetValue(RequestStateProperty) as RequestState)?.Dispose();
            element.ClearValue(RequestStateProperty);
        }
    }

    private sealed class RequestState(FrameworkElement element) : IDisposable
    {
        private FrameworkElement? _visibilityHost;
        private DrawerItemViewModel? _item;
        private IDisposable? _demand;

        internal void Attach()
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            element.DataContextChanged += OnDataContextChanged;
            AttachVisibilityHost();
            RefreshDemand();
        }

        public void Dispose()
        {
            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;
            element.DataContextChanged -= OnDataContextChanged;
            DetachVisibilityHost();
            ReleaseDemand();
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            AttachVisibilityHost();
            RefreshDemand();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            DetachVisibilityHost();
            ReleaseDemand();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            RefreshDemand();
        }

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            RefreshDemand();
        }

        private void AttachVisibilityHost()
        {
            if (!element.IsLoaded)
            {
                return;
            }

            // The image itself is Collapsed until its first icon arrives. Observe
            // the containing grid so this placeholder cannot suppress initial demand.
            var host = VisualTreeHelper.GetParent(element) as FrameworkElement ?? element;
            if (ReferenceEquals(host, _visibilityHost))
            {
                return;
            }

            DetachVisibilityHost();
            _visibilityHost = host;
            host.IsVisibleChanged += OnVisibilityChanged;
        }

        private void DetachVisibilityHost()
        {
            if (_visibilityHost is not null)
            {
                _visibilityHost.IsVisibleChanged -= OnVisibilityChanged;
                _visibilityHost = null;
            }
        }

        private void RefreshDemand()
        {
            var item = element.IsLoaded && _visibilityHost?.IsVisible == true
                ? element.DataContext as DrawerItemViewModel
                : null;
            if (ReferenceEquals(item, _item))
            {
                return;
            }

            ReleaseDemand();
            if (item is not null)
            {
                _item = item;
                _demand = item.AcquireIconDemand();
            }
        }

        private void ReleaseDemand()
        {
            _demand?.Dispose();
            _demand = null;
            _item = null;
        }
    }
}
