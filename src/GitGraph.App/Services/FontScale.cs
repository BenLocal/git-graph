using Avalonia;

namespace GitGraph.App.Services;

/// <summary>
/// View → Font size: one factor for all text, and the row heights that hold it. The sizes are resources the views
/// use as DynamicResource, so changing them here takes effect at once; code that sizes text itself uses <see cref="Of"/>.
/// </summary>
public static class FontScale
{
    public const double Min = 0.8, Max = 1.6, Step = 0.1;

    // The resources Tokens.axaml defines, at 100%.
    private static readonly (string Key, double Size)[] Sizes =
    [
        ("FontSizeSmall", 11.5), ("FontSizeCaption", 12), ("FontSizeBody", 13), ("FontSizeTitle", 15),
        ("FontSizeLarge", 17), ("FontSizeCode", 13), ("DiffLineHeight", 19), ("RowHeight", 26),
    ];

    public static double Current { get; private set; } = 1;

    /// <summary>Raised after the sizes change, for controls that build their text in code.</summary>
    public static event Action? Changed;

    /// <summary>A size at the current scale.</summary>
    public static double Of(double size) => Math.Round(size * Current, 1);

    /// <returns>The scale actually applied (kept within <see cref="Min"/>..<see cref="Max"/>).</returns>
    public static double Apply(double scale)
    {
        Current = Math.Round(Math.Clamp(scale, Min, Max), 2);
        if (Application.Current is { } app)
            foreach (var (key, size) in Sizes) app.Resources[key] = Of(size);
        Changed?.Invoke();
        return Current;
    }
}
