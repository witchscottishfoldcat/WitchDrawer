using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace WitchDrawer.App.Features.ItemContextMenu;

internal interface IFileClipboard
{
    string[] ReadPaths();
    void CopyFile(string path);
    void CopyPath(string path);
}

internal sealed class FileClipboard : IFileClipboard
{
    public string[] ReadPaths() => Clipboard.ContainsFileDropList()
        ? Clipboard.GetFileDropList().Cast<string>().Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    public void CopyFile(string path)
    {
        var files = new StringCollection { path };
        var data = new DataObject();
        data.SetFileDropList(files);
        // DROPEFFECT_COPY: Explorer pastes a copy and leaves the managed file in place.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
        Clipboard.SetDataObject(data, copy: true);
    }

    public void CopyPath(string path) => Clipboard.SetText(path);
}
