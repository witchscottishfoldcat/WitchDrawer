using System.Windows;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Infrastructure;

internal static class FileDropPolicy
{
    internal static DragDropEffects ChooseEffect(BoxType? boxType, DragDropEffects allowedEffects)
    {
        if (boxType == BoxType.Mapping)
        {
            // A mapping stores a reference. Never report Move to the source application.
            if ((allowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            if ((allowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        return boxType is BoxType.Normal or BoxType.Pixel or BoxType.Drawer
            && (allowedEffects & DragDropEffects.Move) != 0
                ? DragDropEffects.Move
                : DragDropEffects.None;
    }
}
