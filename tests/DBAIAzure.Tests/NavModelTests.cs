// Unit tests for the navigation model's active-section / active-sub-view resolution: most-specific
// prefix wins, detail routes activate their section, and every pre-redesign route maps to a section
// (no orphans — FR-012 / SC-003). Pure logic, no I/O.
using DBAIAzure.Web.Navigation;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class NavModelTests
{
    [Theory]
    [InlineData("/", "monitor")]
    [InlineData("/runs", "monitor")]
    [InlineData("/runs/abc123", "monitor")]    // run-history detail keeps Monitor active
    [InlineData("/run/abc123", "monitor")]     // intake run detail keeps Monitor active
    [InlineData("/review-queue", "review")]
    [InlineData("/workflow-builder", "automation")]
    [InlineData("/workflow-gallery", "automation")]
    [InlineData("/settings/connectors", "configuration")]
    [InlineData("/apps", "repos")]
    [InlineData("/apps/xyz", "repos")]          // app detail keeps Repos & Apps active
    [InlineData("/user-guide", "guide")]
    public void ActiveSection_ResolvesExpectedSection(string path, string expectedKey)
    {
        var section = NavModel.ActiveSection(path);

        Assert.NotNull(section);
        Assert.Equal(expectedKey, section!.Key);
    }

    [Fact]
    public void ActiveSection_RootDoesNotSwallowOtherRoutes()
    {
        // The root "/" sub-view must not be the most-specific match for a deeper route.
        Assert.Equal("automation", NavModel.ActiveSection("/workflow-builder")!.Key);
        Assert.Equal("repos", NavModel.ActiveSection("/apps")!.Key);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/runs", "/runs")]
    [InlineData("/runs/abc", "/runs")]          // detail highlights the Run History sub-tab
    [InlineData("/workflow-builder", "/workflow-builder")]
    [InlineData("/workflow-gallery", "/workflow-gallery")]
    public void ActiveSubView_HighlightsExpectedSubTab(string path, string expectedRoute)
    {
        var subView = NavModel.ActiveSubView(path);

        Assert.NotNull(subView);
        Assert.Equal(expectedRoute, subView!.Route);
    }

    [Fact]
    public void EverySection_HasAtLeastOneSubView_AndUserGuideIsSecondary()
    {
        Assert.All(NavModel.Sections, section => Assert.NotEmpty(section.SubViews));

        var userGuide = Assert.Single(NavModel.Sections, section => section.IsSecondary);
        Assert.Equal("guide", userGuide.Key);
    }

    [Fact]
    public void OnlyMultiViewSections_ReportSubTabs()
    {
        // Monitor (Threads + Run History) and Automation (Builder + Gallery) have sub-tabs; the rest don't.
        Assert.True(NavModel.Sections.Single(s => s.Key == "monitor").HasSubTabs);
        Assert.True(NavModel.Sections.Single(s => s.Key == "automation").HasSubTabs);
        Assert.False(NavModel.Sections.Single(s => s.Key == "review").HasSubTabs);
        Assert.False(NavModel.Sections.Single(s => s.Key == "repos").HasSubTabs);
    }
}
