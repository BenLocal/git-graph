using Avalonia.Data.Converters;

namespace GitGraph.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter FileName =
        new FuncValueConverter<string?, string>(p => p is null ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(p)) is { Length: > 0 } n ? n : p);

    /// <summary>Wrapped lines need no sideways scrolling; unwrapped ones do.</summary>
    public static readonly IValueConverter ScrollUnlessWrapped =
        new FuncValueConverter<bool, Avalonia.Controls.Primitives.ScrollBarVisibility>(wrap =>
            wrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

    /// <summary>A menu with items to show: a custom commands submenu hides when there are none.</summary>
    public static readonly IValueConverter HasItems = new FuncValueConverter<System.Collections.ICollection?, bool>(c => c is { Count: > 0 });

    public static readonly IValueConverter DimIfTrue = new FuncValueConverter<bool, double>(b => b ? 0.5 : 1.0);

    /// <summary>Checks a radio menu item when the bound value reads the same as the ConverterParameter.</summary>
    public static readonly IValueConverter IsValue =
        new FuncValueConverter<object?, object?, bool>((v, p) => string.Equals(v?.ToString(), p?.ToString(), StringComparison.Ordinal));
}
