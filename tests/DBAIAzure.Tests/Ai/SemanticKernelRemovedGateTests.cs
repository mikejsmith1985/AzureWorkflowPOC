// Cutover gate (spec-019 T050 / SC-002): the atomic cutover removed the Semantic Kernel Process Framework.
// This test fails if any Microsoft.SemanticKernel package reference or SKEXP pragma creeps back into the
// solution, so the codebase stays on GA/stable MAF packages only.
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Scans the whole solution tree for any lingering Semantic Kernel reference. The migration replaced the SK
/// Process Framework with MAF Workflows (spec-019); nothing under <c>src/</c> or <c>tests/</c> may reference
/// <c>Microsoft.SemanticKernel</c> or an <c>SKEXP</c> experimental pragma again.
/// </summary>
public sealed class SemanticKernelRemovedGateTests
{
    private static readonly Regex Banned = new(@"Microsoft\.SemanticKernel|SKEXP[0-9]", RegexOptions.Compiled);

    [Fact]
    public void Solution_HasNoSemanticKernelReferences()
    {
        var root = FindRepoRoot();
        var offenders = new[] { "src", "tests" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".csproj"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            // This gate test necessarily mentions the banned strings; exclude it from its own scan.
            .Where(path => !path.EndsWith("SemanticKernelRemovedGateTests.cs"))
            .Where(path => Banned.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Semantic Kernel must be fully removed (spec-019 T050). Offending files:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Walks up from the test assembly to the repo root (the folder containing the .sln).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.sln").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate the solution root.");
    }
}
