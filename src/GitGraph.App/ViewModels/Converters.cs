using Avalonia.Data.Converters;

namespace GitGraph.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter FileName =
        new FuncValueConverter<string?, string>(p => p is null ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(p)) is { Length: > 0 } n ? n : p);

    public static readonly IValueConverter DimIfTrue = new FuncValueConverter<bool, double>(b => b ? 0.5 : 1.0);
}
