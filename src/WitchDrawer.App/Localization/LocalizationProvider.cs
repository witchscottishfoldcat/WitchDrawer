using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.Core.Localization;

namespace WitchDrawer.App.Localization;

public sealed class LocalizationProvider : INotifyPropertyChanged
{
    public static LocalizationProvider Instance { get; } = new();
    private LocalizationProvider() => Strings.LanguageChanged += OnLanguageChanged;

    public string this[string key] => Strings.Get(key);
    public string Language => Strings.Culture.Name;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(string language) => Strings.SetLanguage(language);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            WeakReferenceMessenger.Default.Send(new LanguageChangedMessage());
        }

        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
            app.Dispatcher.Invoke(Refresh);
        else Refresh();
    }
}

internal sealed record LanguageChangedMessage;
