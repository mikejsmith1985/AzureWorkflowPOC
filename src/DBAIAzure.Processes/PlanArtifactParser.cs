// Pure markdown parser turning Plan-phase artifacts into the planned units of work the handler creates.
using DBAIAzure.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace DBAIAzure.Processes;

/// <summary>
/// Parses Plan-phase artifacts into <see cref="PlannedWorkItem"/>s (one per planned unit of work,
/// FR-008). When the feature has a <c>tasks.md</c> with a manageable number of items, those become
/// the ADO Tasks; when <c>tasks.md</c> has more items than <see cref="MaxPlanTasksFromTasksMd"/>
/// (it was produced by the later <c>/speckit.tasks</c> phase and is too granular for Plan-level
/// board items), the parser falls back to structural <c>##</c>/<c>###</c> headings of <c>plan.md</c>.
/// When neither yields any item, an empty list is returned. This is pure — text in, items out.
/// </summary>
public static class PlanArtifactParser
{
    private const string TasksFileName = "tasks.md";
    private const string PlanFileName = "plan.md";

    /// <summary>
    /// Maximum checkbox tasks from tasks.md before we fall back to plan.md section headings.
    /// tasks.md is produced by /speckit.tasks (a downstream phase) and can have 50+ implementation
    /// tasks — far too granular for Plan-phase ADO work items. plan.md sections are the right
    /// granularity for a board that tracks deliverables, not implementation steps.
    /// </summary>
    private const int MaxPlanTasksFromTasksMd = 20;

    /// <summary>
    /// Matches a markdown task line such as <c>- [ ] T001 [P] Do the thing</c>, capturing the
    /// description after the checkbox. The optional <c>[P]</c> parallel marker is stripped later.
    /// </summary>
    private static readonly Regex TaskLinePattern = new(
        @"^\s*[-*]\s*\[[ xX]\]\s*(?<body>.+?)\s*$",
        RegexOptions.Compiled);

    /// <summary>Matches a leading <c>[P]</c> (or similar single-token) parallel marker to strip it.</summary>
    private static readonly Regex ParallelMarkerPattern = new(
        @"\[(?:P|p)\]\s*",
        RegexOptions.Compiled);

    /// <summary>Matches a markdown section heading (<c>##</c> or deeper), capturing its title text.</summary>
    private static readonly Regex HeadingPattern = new(
        @"^\s*(?<hashes>#{2,6})\s+(?<title>.+?)\s*#*\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns one planned unit of work per task line in <c>tasks.md</c> when present, otherwise one
    /// per structural heading in <c>plan.md</c>. Returns an empty list when neither yields any unit.
    /// </summary>
    public static IReadOnlyList<PlannedWorkItem> Parse(IReadOnlyList<PhaseArtifact> artifacts)
    {
        var tasksArtifact = FindArtifact(artifacts, TasksFileName);
        if (tasksArtifact is not null)
        {
            var fromTasks = ParseTaskLines(tasksArtifact.Content);
            // Only use tasks.md when it is at plan-level granularity; a large tasks.md (e.g. produced
            // by /speckit.tasks) would flood the board with implementation-level items. Fall through to
            // plan.md sections — those are the right granularity for ADO Plan deliverables.
            if (fromTasks.Count > 0 && fromTasks.Count <= MaxPlanTasksFromTasksMd) return fromTasks;
        }

        var planArtifact = FindArtifact(artifacts, PlanFileName);
        return planArtifact is null ? [] : ParsePlanSections(planArtifact.Content);
    }

    // ── tasks.md parsing ────────────────────────────────────────────────────────

    /// <summary>Turns each checkbox task line into a planned item; non-task lines are ignored.</summary>
    private static List<PlannedWorkItem> ParseTaskLines(string content)
    {
        var items = new List<PlannedWorkItem>();
        foreach (var line in SplitLines(content))
        {
            var match = TaskLinePattern.Match(line);
            if (!match.Success) continue;

            var title = ParallelMarkerPattern.Replace(match.Groups["body"].Value, string.Empty).Trim();
            if (title.Length == 0) continue;

            items.Add(new PlannedWorkItem { Title = title, Description = title });
        }
        return items;
    }

    // ── plan.md fallback parsing ──────────────────────────────────────────────────

    /// <summary>
    /// Splits <c>plan.md</c> at each <c>##</c>/<c>###</c> heading: the heading text is the item title
    /// and the prose until the next heading is its description.
    /// </summary>
    private static List<PlannedWorkItem> ParsePlanSections(string content)
    {
        var items = new List<PlannedWorkItem>();
        string? currentTitle = null;
        var body = new StringBuilder();

        foreach (var line in SplitLines(content))
        {
            var heading = HeadingPattern.Match(line);
            if (heading.Success)
            {
                FlushSection(items, currentTitle, body);
                currentTitle = heading.Groups["title"].Value.Trim();
                body.Clear();
                continue;
            }

            if (currentTitle is not null) body.AppendLine(line);
        }

        FlushSection(items, currentTitle, body);
        return items;
    }

    /// <summary>Appends the accumulated section as a planned item when a heading is in progress.</summary>
    private static void FlushSection(List<PlannedWorkItem> items, string? title, StringBuilder body)
    {
        if (title is null) return;
        var description = body.ToString().Trim();
        items.Add(new PlannedWorkItem
        {
            Title = title,
            Description = description.Length == 0 ? title : description,
        });
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Finds an artifact by file name (case-insensitive), or null.</summary>
    private static PhaseArtifact? FindArtifact(IReadOnlyList<PhaseArtifact> artifacts, string fileName) =>
        artifacts.FirstOrDefault(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Splits text into lines, tolerating both Windows and Unix newlines.</summary>
    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Split('\n');
}
