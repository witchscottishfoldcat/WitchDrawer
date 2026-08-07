using System;

namespace WitchDrawer.App.Messages;

/// <summary>
/// Published by desktop box windows whenever the occupied grid extent changes,
/// so settings surfaces can validate fixed sizes against the real content.
/// </summary>
public sealed record BoxGridExtentChangedMessage(Guid BoxId, int Columns, int Rows);
