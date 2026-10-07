using WitchDrawer.Core.Localization;
using System.IO;
using System.Windows;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Features.ItemContextMenu;

public partial class FileRenameWindow : Window
{
    internal FileRenameWindow(string name, bool isMappingBox, bool isDirectory = false)
    {
        InitializeComponent();
        Title = isMappingBox ? Strings.Get("RenameReference") : isDirectory ? Strings.Get("RenameFolder") : Strings.Get("RenameFile");
        Description.Text = isMappingBox ? Strings.Get("OnlyTheReferenceNameChangesTheSourceFileStays")
            : isDirectory ? Strings.Get("EnterANewFolderName") : Strings.Get("EnterANewNameIncludingTheFileExtension");
        NameInput.Text = name;
        Loaded += (_, _) =>
        {
            NameInput.Focus();
            var extension = isDirectory ? "" : Path.GetExtension(name);
            NameInput.Select(0, extension.Length > 0 ? name.Length - extension.Length : name.Length);
        };
    }

    internal string NewName => NameInput.Text;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        try
        {
            DrawerService.ValidateItemName(NewName);
            DialogResult = true;
        }
        catch (ArgumentException exception) { ErrorText.Text = exception.Message; }
    }
}
