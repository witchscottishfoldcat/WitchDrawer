using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Views;

public partial class AboutPageView : UserControl
{
    internal const string SupportPageUri = "https://www.witchcat.cn/zh/support";

    /// <summary>
    /// Owned by the hosting window; assigned right after startup so failure logging
    /// keeps going through the same application logger. Static because the view is
    /// hosted by LazyPageHost and is not instantiated until first shown.
    /// </summary>
    internal static IAppLogger? Logger { get; set; }

    public AboutPageView()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnOpenProjectLinkClicked(object sender, RoutedEventArgs e)
    {
        OpenExternalUri("https://github.com/witchscottishfoldcat/WitchDrawer");
    }

    private void OnOpenEmailClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri("mailto:witchscottishfoldcat@gmail.com");
    }

    private void OnOpenWebsiteClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri("https://www.witchcat.cn");
    }

    private void OnOpenSupportLinkClicked(object sender, RoutedEventArgs e)
    {
        OpenExternalUri(SupportPageUri);
    }

    private async void OnExportDiagnosticLogsClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var privacyConfirmation = MessageBox.Show(
            owner,
            "诊断包只包含最近的运行日志和基础环境信息，不包含数据库或用户文件内容。\n\n日志中可能包含文件名和完整路径，发送前请按需检查。是否继续？",
            "导出诊断日志",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (privacyConfirmation != MessageBoxResult.OK)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存 WitchDrawer 诊断日志",
            Filter = "ZIP 压缩包 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"WitchDrawer-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        ExportDiagnosticLogsButton.IsEnabled = false;
        try
        {
            var result = await ViewModel.Maintenance.ExportDiagnosticLogsAsync(dialog.FileName);
            MessageBox.Show(
                owner,
                $"诊断日志已导出。\n\n包含日志：{result.LogFileCount} 个\n保存位置：{result.ArchivePath}",
                "导出完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                owner,
                "诊断日志导出失败：\n" + exception.Message,
                "导出失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportDiagnosticLogsButton.IsEnabled = true;
        }
    }

    private void OpenExternalUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger?.Error(exception, $"Failed to open external URI: {uri}");
        }
    }
}
