using System;

namespace WitchDrawer.App.Messages;

public sealed record BoxSizeModeChangedMessage(Guid BoxId, bool IsFixed, int Columns, int Rows);
