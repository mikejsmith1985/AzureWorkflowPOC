// Unit tests for TooltipService: Show publishes a context + fires OnChange, Hide clears it, a second
// Show replaces the first, and the flip rule (above/below) follows the anchor's viewport position.
using DBAIAzure.Web.Services;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class TooltipServiceTests
{
    private static TooltipAnchor AnchorAt(double top, double viewportHeight = 1000) =>
        new(Left: 100, Top: top, Bottom: top + 16, ViewportHeight: viewportHeight);

    [Fact]
    public void Show_SetsActiveAndFiresOnChange()
    {
        var service = new TooltipService();
        var fired = 0;
        service.OnChange += () => fired++;

        service.Show("Instance URL", "https://dev.example.com", AnchorAt(100));

        Assert.NotNull(service.Active);
        Assert.Equal("Instance URL", service.Active!.Content);
        Assert.Equal("https://dev.example.com", service.Active.Example);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Hide_ClearsActiveAndFiresOnChange()
    {
        var service = new TooltipService();
        service.Show("x", null, AnchorAt(100));
        var fired = 0;
        service.OnChange += () => fired++;

        service.Hide();

        Assert.Null(service.Active);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SecondShow_ReplacesFirst()
    {
        var service = new TooltipService();
        service.Show("first", null, AnchorAt(100));

        service.Show("second", "ex", AnchorAt(200));

        Assert.Equal("second", service.Active!.Content);
        Assert.Equal("ex", service.Active.Example);
    }

    [Theory]
    [InlineData(100, true)]   // icon in top half → panel below
    [InlineData(900, false)]  // icon in bottom half → panel above
    public void ShowBelow_FollowsViewportHalf(double top, bool expectedBelow)
    {
        var context = new TooltipContext("c", null, AnchorAt(top, viewportHeight: 1000));
        Assert.Equal(expectedBelow, context.ShowBelow);
    }
}
