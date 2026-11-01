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
    [InlineData("2026-11-01", 0, 1)]
    [InlineData("2026-11-01", 7, 1)]
    [InlineData("2026-11-01", 14, 1)]
    [InlineData("2026-11-01", 365, 1)]
    [InlineData("2026-11-01", 0, 1.6)]
    [InlineData("2026-11-01", 365, 1.6)]
    [InlineData("2027-01-03", 0, 1)]
    [InlineData("2027-01-03", 365, 1)]
    [InlineData("2027-01-03", 0, 1.6)]
    [InlineData("2027-01-03", 365, 1.6)]
    public Task Labels_fit_inside_the_measured_control_at_month_and_year_boundaries(string today, int ageDays, double fontScale) => Ui.Dispatch<bool>(() =>
    {
        var previousScale = FontScale.Current;
        try
        {
            FontScale.Apply(fontScale);
            var clock = new FixedTimeProvider(DateOnly.ParseExact(today, "yyyy-MM-dd"));
            var first = clock.GetUtcNow().AddDays(-ageDays);
            var heatmap = new ContributionHeatmap(clock)
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

    private sealed class FixedTimeProvider(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
