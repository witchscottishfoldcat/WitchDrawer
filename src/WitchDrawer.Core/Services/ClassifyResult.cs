namespace WitchDrawer.Core.Services;

/// <summary>
/// Result of a one-click classification pass.
/// </summary>
public sealed record ClassifyResult(int MovedCount, int SkippedCount);
