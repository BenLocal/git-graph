using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace GitGraph.App.Controls;

/// <summary>
/// Short, cheap animations that answer a user action. Set GITGRAPH_REDUCED_MOTION=1 to turn them off;
/// Avalonia doesn't expose the desktop's reduced-motion setting. Snapshots have none, so they never catch a frame mid-fade.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("GITGRAPH_REDUCED_MOTION") != "1"
                                          && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT"));

    private static readonly Animation FadeAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.25) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
        },
    };

    /// <summary>Fades a control in, to show that its content was just replaced.</summary>
    public static void FadeIn(Animatable? target)
    {
        if (!Enabled || target is null) return;
        _ = FadeAnimation.RunAsync(target);
    }
}
