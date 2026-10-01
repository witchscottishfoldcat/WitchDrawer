using System.Windows.Controls;
using System.Windows.Input;

namespace WitchDrawer.App.Views;

public partial class ThemeCustomizationView : UserControl
{
    public ThemeCustomizationView() => InitializeComponent();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox input) return;
        input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        input.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        e.Handled = true;
    }
}
