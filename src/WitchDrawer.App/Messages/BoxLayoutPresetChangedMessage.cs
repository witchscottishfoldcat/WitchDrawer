using System;

namespace WitchDrawer.App.Messages;

public sealed record BoxLayoutPresetChangedMessage(Guid BoxId, string Preset);
