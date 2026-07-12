// bUnit tests for the shared EmptyState.razor component (spec-014 T036 / FR-022):
// verifies the consistent "nothing here yet" treatment renders its title, optional
// description, optional call-to-action, and an accessible icon.
using Bunit;
using DBAIAzure.Web.Shared;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Component tests for the console-wide empty-state placeholder. FR-022 requires every empty
/// list or panel to show the same friendly message rather than a blank region, so these tests
/// pin the component's structure: a title is always shown, a description and action are optional,
/// and the icon is exposed to assistive technology.
/// </summary>
public sealed class EmptyStateComponentTests : TestContext
{
    // ── Title is mandatory and always rendered ───────────────────────────────

    [Fact]
    public void EmptyState_RendersTitle()
    {
        var cut = RenderComponent<EmptyState>(parameters => parameters
            .Add(component => component.Title, "No workflows yet"));

        Assert.Contains("No workflows yet", cut.Markup);
        // The shared treatment is discoverable by tests/E2E via a stable hook.
        Assert.NotNull(cut.Find("[data-testid=\"empty-state\"]"));
    }

    // ── Description is optional: absent unless supplied ──────────────────────

    [Fact]
    public void EmptyState_WhenNoDescription_RendersNoDescriptionParagraph()
    {
        var cut = RenderComponent<EmptyState>(parameters => parameters
            .Add(component => component.Title, "No apps yet"));

        Assert.DoesNotContain("empty-state-description", cut.Markup);
    }

    [Fact]
    public void EmptyState_WhenDescriptionProvided_RendersIt()
    {
        var cut = RenderComponent<EmptyState>(parameters => parameters
            .Add(component => component.Title, "No apps yet")
            .Add(component => component.Description, "Register a repo to build, run, and monitor it."));

        Assert.Contains("Register a repo to build, run, and monitor it.", cut.Markup);
    }

    // ── Optional call-to-action renders inside the treatment ─────────────────

    [Fact]
    public void EmptyState_RendersChildContentAction()
    {
        var cut = RenderComponent<EmptyState>(parameters => parameters
            .Add(component => component.Title, "No workflows yet")
            .AddChildContent("<a href=\"/workflow-builder\">Start Building</a>"));

        Assert.Contains("Start Building", cut.Markup);
    }

    // ── Icon carries an accessible label for screen readers ──────────────────

    [Fact]
    public void EmptyState_IconExposesAccessibleLabel()
    {
        var cut = RenderComponent<EmptyState>(parameters => parameters
            .Add(component => component.Title, "Nothing here")
            .Add(component => component.IconLabel, "no runs"));

        var icon = cut.Find("[role=\"img\"]");
        Assert.Equal("no runs", icon.GetAttribute("aria-label"));
    }
}
