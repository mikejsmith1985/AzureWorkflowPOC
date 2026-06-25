// Ecosystem-based build-command auto-detection for a target repo (feature 013, R3).
namespace DBAIAzure.Connectors.Apps;

/// <summary>
/// Resolves a sensible build command from a repository's contents when the operator did not supply
/// one (FR-005), mirroring the reference application's pip/npm auto-detection. Returns null when no
/// ecosystem is recognized, so the caller can fail the build with an explanatory summary.
/// </summary>
public static class BuildCommandAutoDetector
{
    /// <summary>
    /// Inspects <paramref name="repoPath"/> and returns a build command, or null if none can be
    /// confidently inferred. Detection order: Node, Python, .NET, then a bare Dockerfile.
    /// </summary>
    public static string? Detect(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return null;

        if (File.Exists(Path.Combine(repoPath, "package.json")))
            return "npm ci";

        if (File.Exists(Path.Combine(repoPath, "requirements.txt")))
            return "pip install -r requirements.txt";

        if (File.Exists(Path.Combine(repoPath, "pyproject.toml")))
            return "pip install .";

        if (HasAnyFile(repoPath, "*.sln") || HasAnyFile(repoPath, "*.csproj"))
            return "dotnet build";

        if (File.Exists(Path.Combine(repoPath, "Dockerfile")))
            return "docker build -t app .";

        return null;
    }

    private static bool HasAnyFile(string repoPath, string pattern) =>
        Directory.EnumerateFiles(repoPath, pattern, SearchOption.TopDirectoryOnly).Any();
}
