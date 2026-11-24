using WitchDrawer.Core.Localization;

namespace WitchDrawer.Core.Models;

public sealed class BoxFullException() : InvalidOperationException(Strings.Get("BoxIsFullCannotAddFiles"));
