using WitchDrawer.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Views;

public partial class BoxDisplaySettingsView : UserControl
{
    public sealed class Choice(object value, string labelKey) : WitchDrawer.App.Localization.LocalizedObservableObject
    {
        public object Value { get; } = value;
        public string Label => Strings.Get(labelKey);
    }
    public static IReadOnlyList<Choice> IconSizeChoices { get; } =
        [new("3x3", "ExtraLarge"), new("4x4", "L"), new("5x5", "M"), new("6x6", "S")];
    public static IReadOnlyList<Choice> EnabledChoices { get; } = [new(false, "Off"), new(true, "On")];
    public static IReadOnlyList<Choice> VisibilityChoices { get; } = [new(true, "Show"), new(false, "Hide")];
    public static IReadOnlyList<Choice> SizeModeChoices { get; } = [new(false, "AutoSize"), new(true, "FixedGrid")];
    internal IAppLogger? Logger { get; set; }

    public BoxDisplaySettingsView() => InitializeComponent();

    internal Size MeasureExpandedSize(double availableWidth)
    {
        var visibility = FixedSizeControls.Visibility;
        try
        {
            // Measure the largest state without changing the user's size mode
            // or replacing the visibility binding. The popup can reserve screen
            // space while the actual panel still grows naturally downwards.
            FixedSizeControls.SetCurrentValue(VisibilityProperty, Visibility.Visible);
            Measure(new Size(availableWidth, double.PositiveInfinity));
            return DesiredSize;
        }
        finally
        {
            FixedSizeControls.SetCurrentValue(VisibilityProperty, visibility);
            InvalidateMeasure();
        }
    }

    private async void OnChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox choice || !ReferenceEquals(e.Source, choice)
            || DataContext is not MainViewModel { SelectedBox: { } box } model)
            return;

        try
        {
            // Binding updates also raise SelectionChanged. Only a different value
            // represents a user edit; loading a box must never write its settings.
            switch (choice.Name)
            {
                case "VisualStyleChoice" when choice.SelectedItem is BoxVisualStyleOption option && option.Style != box.VisualStyle:
                    if (model.SetSelectedBoxVisualStyleCommand.CanExecute(option))
                        await model.SetSelectedBoxVisualStyleCommand.ExecuteAsync(option);
                    break;
                case "IconSizeChoice" when choice.SelectedValue is string preset && preset != box.LayoutSettings.CurrentPreset:
                    await box.LayoutSettings.ApplyPresetCommand.ExecuteAsync(preset);
                    break;
                case "HoverChoice" when choice.SelectedValue is bool enabled && enabled != box.IsHoverRollUpEnabled:
                    await box.ToggleHoverRollUpCommand.ExecuteAsync(null);
                    break;
                case "TitleChoice" when choice.SelectedValue is bool visible && visible != box.IsTitleVisible:
                    await box.ToggleTitleVisibilityCommand.ExecuteAsync(null);
                    break;
                case "FileNameChoice" when choice.SelectedValue is bool visible && visible != box.IsFileNameVisible:
                    await box.ToggleFileNameVisibilityCommand.ExecuteAsync(null);
                    break;
                case "SizeModeChoice" when choice.SelectedValue is bool fixedMode && fixedMode != model.BoxSizeSettings.IsFixedMode:
                    await (fixedMode ? model.BoxSizeSettings.UseFixedModeCommand : model.BoxSizeSettings.UseAdaptiveModeCommand).ExecuteAsync(null);
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger?.Error(exception, "Failed to apply box display settings.");
            model.ReportStatus(exception.Message);
        }
        finally
        {
            choice.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
        }
    }
}
