using WitchDrawer.App;

namespace WitchDrawer.App.Tests;

public sealed class LayoutBackupSlotPresentationTests
{
    [Fact]
    public void EmptySlot_ShowsRecordOnly()
    {
        var presentation = MainWindow.GetLayoutBackupSlotPresentation(hasBackup: false);

        Assert.Equal("未记录", presentation.StatusText);
        Assert.Equal("记录", presentation.RecordButtonText);
        Assert.False(presentation.CanRestore);
        Assert.False(presentation.CanDelete);
    }

    [Fact]
    public void RecordedSlot_ShowsRecordedOverwriteAndRestore()
    {
        var presentation = MainWindow.GetLayoutBackupSlotPresentation(hasBackup: true);

        Assert.Equal("已记录", presentation.StatusText);
        Assert.Equal("覆盖", presentation.RecordButtonText);
        Assert.True(presentation.CanRestore);
        Assert.True(presentation.CanDelete);
    }
}
