using System.Windows;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class FileDropPolicyTests
{
    [Theory]
    [InlineData(BoxType.Normal, DragDropEffects.Copy, DragDropEffects.None)]
    [InlineData(BoxType.Normal, DragDropEffects.Link, DragDropEffects.None)]
    [InlineData(BoxType.Normal, DragDropEffects.All, DragDropEffects.Move)]
    [InlineData(BoxType.Pixel, DragDropEffects.Move, DragDropEffects.Move)]
    [InlineData(BoxType.Drawer, DragDropEffects.Copy, DragDropEffects.None)]
    [InlineData(BoxType.Drawer, DragDropEffects.All, DragDropEffects.Move)]
    [InlineData(BoxType.Mapping, DragDropEffects.Move, DragDropEffects.None)]
    [InlineData(BoxType.Mapping, DragDropEffects.Copy, DragDropEffects.Copy)]
    [InlineData(BoxType.Mapping, DragDropEffects.Link, DragDropEffects.Link)]
    [InlineData(BoxType.Mapping, DragDropEffects.All, DragDropEffects.Copy)]
    [InlineData(BoxType.Mapping, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, DragDropEffects.Link)]
    [InlineData(BoxType.Mapping, DragDropEffects.None, DragDropEffects.None)]
    [InlineData(BoxType.Todo, DragDropEffects.All, DragDropEffects.None)]
    [InlineData(null, DragDropEffects.All, DragDropEffects.None)]
    public void FileDrop_MatchesPhysicalOperationAndSourcePermissions(BoxType? type, DragDropEffects allowed, DragDropEffects expected)
    {
        Assert.Equal(expected, FileDropPolicy.ChooseEffect(type, allowed));
    }
}
