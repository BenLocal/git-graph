using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GitGraph.App.Controls;

/// <summary>Line colours for graph lanes, chosen to read on both the night and day canvas.</summary>
public static class LanePalette
{
    private static readonly Color[] Colors =
    [
        Avalonia.Media.Color.Parse("#E4572E"), // vermilion
        Avalonia.Media.Color.Parse("#2EA6A0"), // teal
        Avalonia.Media.Color.Parse("#5B7BE0"), // cobalt
        Avalonia.Media.Color.Parse("#B46CD6"), // violet
        Avalonia.Media.Color.Parse("#6DAF3F"), // moss
        Avalonia.Media.Color.Parse("#E06C9F"), // rose
        Avalonia.Media.Color.Parse("#3BB3E0"), // sky
    ];

    private static readonly IImmutableSolidColorBrush[] Brushes =
        Colors.Select(c => (IImmutableSolidColorBrush)new ImmutableSolidColorBrush(c)).ToArray();

    public static Color Color(int index) => Colors[Mod(index)];

    public static IImmutableSolidColorBrush Brush(int index) => Brushes[Mod(index)];

    private static int Mod(int i) => ((i % Colors.Length) + Colors.Length) % Colors.Length;
}
