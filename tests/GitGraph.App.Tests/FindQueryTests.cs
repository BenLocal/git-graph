using GitGraph.App.ViewModels;

namespace GitGraph.App.Tests;

/// <summary>The find box's filter syntax: author:, path:, code:, since: and plain text.</summary>
public sealed class FindQueryTests
{
    [Fact]
    public void Filters_come_out_of_the_text_and_the_rest_stays()
    {
        var q = FindQuery.Parse("tiles author:ana path:src/ code:\"var x = 1\" since:1m cache");
        Assert.Equal(("tiles cache", "ana", "src/", "var x = 1", "Last month"), (q.Text, q.Author, q.Path, q.Code, q.Since));
        Assert.True(q.HasFilters);
        Assert.Null(q.Problem);
    }

    [Fact]
    public void Aliases_quotes_and_case_are_understood()
    {
        var q = FindQuery.Parse("AUTHOR:\"Ana Souza\" file:README.md contents:Foo since:quarter");
        Assert.Equal(("", "Ana Souza", "README.md", "Foo", "Last 3 months"), (q.Text, q.Author, q.Path, q.Code, q.Since));
    }

    [Fact]
    public void Plain_text_and_other_colons_are_left_alone()
    {
        var q = FindQuery.Parse("fix: crash on start http://example.com");
        Assert.Equal("fix: crash on start http://example.com", q.Text);
        Assert.False(q.HasFilters);
    }

    [Fact]
    public void An_unknown_period_is_a_problem_and_nothing_is_taken_out()
    {
        var q = FindQuery.Parse("since:fortnight bug");
        Assert.Equal("since:fortnight bug", q.Text);
        Assert.False(q.HasFilters);
        Assert.Contains("fortnight", q.Problem);
    }

    [Theory]
    [InlineData("week", "Last week")]
    [InlineData("7d", "Last week")]
    [InlineData("1.month", "Last month")]
    [InlineData("3months", "Last 3 months")]
    [InlineData("1y", "Last year")]
    [InlineData("all", "Any time")]
    public void Periods_have_short_forms(string value, string option)
    {
        Assert.Equal(option, FindQuery.Parse($"since:{value}").Since);
    }
}
