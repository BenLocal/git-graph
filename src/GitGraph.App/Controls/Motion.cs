using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace GitGraph.App.Controls;

/// <summary>
/// Render-thread composition animations that answer a user action without blocking on UI layout.
/// Set GITGRAPH_REDUCED_MOTION=1 to turn them off; snapshots have none, so they never catch a frame mid-fade.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("GITGRAPH_REDUCED_MOTION") != "1"
                                          && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT"));

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(160);
    private static readonly CubicEaseOut EaseOut = new();

    /// <summary>Fades and nudges a visual in on the render thread when its content is replaced.</summary>
    public static void FadeIn(Visual? target)
    {
        if (!Enabled || target is null) return;
        Run(target, startOpacity: 0.25f, offsetX: 0f, offsetY: 5f, deferToRender: false);
    }

    /// <summary>Slides and fades a newly revealed panel in on the render thread.</summary>
    public static void SlideIn(Visual? target, float offsetX, float offsetY)
    {
        if (!Enabled || target is null) return;
        Run(target, startOpacity: 0f, offsetX, offsetY, deferToRender: true);
    }

    private static void Run(Visual target, float startOpacity, float offsetX, float offsetY, bool deferToRender)
    {
        if (deferToRender)
        {
            Dispatcher.UIThread.Post(() => StartComposition(target, startOpacity, offsetX, offsetY), DispatcherPriority.Render);
            return;
        }
        StartComposition(target, startOpacity, offsetX, offsetY);
    }

    private static void StartComposition(Visual target, float startOpacity, float offsetX, float offsetY)
    {
        if (!target.IsVisible || ElementComposition.GetElementVisual(target) is not { } visual) return;
        var compositor = visual.Compositor;

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = Duration;
        opacity.InsertKeyFrame(0f, startOpacity, EaseOut);
        opacity.InsertKeyFrame(1f, 1f, EaseOut);
        visual.StartAnimation("Opacity", opacity);

        if (offsetX == 0f && offsetY == 0f) return;
        var end = visual.Offset;
        var start = new Vector3((float)end.X + offsetX, (float)end.Y + offsetY, (float)end.Z);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = Duration;
        offset.InsertKeyFrame(0f, start, EaseOut);
        offset.InsertKeyFrame(1f, new Vector3((float)end.X, (float)end.Y, (float)end.Z), EaseOut);
        visual.StartAnimation("Offset", offset);
    }
}
