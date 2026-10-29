using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using GitGraph.App.Controls;
using GitGraph.App.Services;

namespace GitGraph.App.Tests;

public sealed class ContributionHeatmapTests
{
    private static HeadlessUnitTestSession Ui => TerminalTestApp.Ui;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 1)]
    [InlineData(14, 1)]
    [InlineData(365, 1)]
    [InlineData(0, 1.6)]
    [InlineData(365, 1.6)]
    public Task Legend_fits_inside_the_measured_control_for_short_and_long_histories(int ageDays, double fontScale) => Ui.Dispatch<bool>(() =>
    {
        var previousScale = FontScale.Current;
        try
        {
            FontScale.Apply(fontScale);
            var first = DateTimeOffset.Now.AddDays(-ageDays);
            var heatmap = new ContributionHeatmap
            {
                From = first,
                Days = new Dictionary<DateOnly, int> { [DateOnly.FromDateTime(first.DateTime)] = 3 },
            };
            heatmap.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            heatmap.Arrange(new Rect(heatmap.DesiredSize));
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) heatmap.Render(context);

            var bounds = drawing.GetBounds();
            Assert.True(bounds.Left >= 0, $"Drawing starts at {bounds.Left}; the legend extends outside the control.");
            Assert.True(bounds.Right <= heatmap.Bounds.Width, $"Drawing ends at {bounds.Right}, beyond width {heatmap.Bounds.Width}.");
            return true;
        }
        finally { FontScale.Apply(previousScale); }
    }, CancellationToken.None);
}
