using Avalonia.Data.Converters;

namespace GitGraph.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter FileName =
        new FuncValueConverter<string?, string>(p => p is null ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(p)) is { Length: > 0 } n ? n : p);

    public static readonly IValueConverter DimIfTrue = new FuncValueConverter<bool, double>(b => b ? 0.5 : 1.0);

    /// <summary>Checks a radio menu item when the bound value reads the same as the ConverterParameter.</summary>
    public static readonly IValueConverter IsValue =
        new FuncValueConverter<object?, object?, bool>((v, p) => string.Equals(v?.ToString(), p?.ToString(), StringComparison.Ordinal));
}
