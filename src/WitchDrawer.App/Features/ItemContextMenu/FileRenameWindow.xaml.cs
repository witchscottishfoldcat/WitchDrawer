using System.IO;
using System.Windows;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Features.ItemContextMenu;

public partial class FileRenameWindow : Window
{
    internal FileRenameWindow(string name, bool isMappingBox, bool isDirectory = false)
    {
        InitializeComponent();
        Title = isMappingBox ? "重命名引用" : isDirectory ? "重命名文件夹" : "重命名文件";
        Description.Text = isMappingBox ? "只修改引用名称，源文件保持原样。"
            : isDirectory ? "输入新的文件夹名称：" : "输入新名称（包含文件扩展名）：";
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
