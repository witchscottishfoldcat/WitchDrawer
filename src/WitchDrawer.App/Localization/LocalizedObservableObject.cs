using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace WitchDrawer.App.Localization;

/// <summary>Refresh localized presentation without retaining closed boxes or recycled items.</summary>
public abstract class LocalizedObservableObject : ObservableObject
{
    protected LocalizedObservableObject()
    {
        _ = LocalizationProvider.Instance;
        WeakReferenceMessenger.Default.Register<LocalizedObservableObject, LanguageChangedMessage>(
            this, static (recipient, _) =>
            {
                recipient.OnLanguageChanged();
                recipient.OnPropertyChanged(string.Empty);
            });
    }

    protected virtual void OnLanguageChanged() { }
}
