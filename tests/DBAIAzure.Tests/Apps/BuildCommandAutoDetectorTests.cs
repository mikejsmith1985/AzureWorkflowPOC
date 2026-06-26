// Unit tests for BuildCommandAutoDetector ecosystem heuristics (feature 013, US2).
using DBAIAzure.Connectors.Apps;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>Verifies build-command auto-detection picks the right ecosystem command, or null when none match.</summary>
public sealed class BuildCommandAutoDetectorTests
{
    private static DirectoryInfo RepoWith(params string[] fileNames)
    {
        var dir = Directory.CreateTempSubdirectory("autodetect-");
        foreach (var name in fileNames)
            File.WriteAllText(Path.Combine(dir.FullName, name), "x");
        return dir;
    }

    [Fact]
    public void Detect_NodeProject_ReturnsNpm()
    {
        var dir = RepoWith("package.json");
        try { Assert.Equal("npm ci", BuildCommandAutoDetector.Detect(dir.FullName)); }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Detect_PythonRequirements_ReturnsPip()
    {
        var dir = RepoWith("requirements.txt");
        try { Assert.Equal("pip install -r requirements.txt", BuildCommandAutoDetector.Detect(dir.FullName)); }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Detect_DotnetProject_ReturnsDotnetBuild()
    {
        var dir = RepoWith("App.csproj");
        try { Assert.Equal("dotnet build", BuildCommandAutoDetector.Detect(dir.FullName)); }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Detect_DockerfileOnly_ReturnsDockerBuild()
    {
        var dir = RepoWith("Dockerfile");
        try { Assert.Equal("docker build -t app .", BuildCommandAutoDetector.Detect(dir.FullName)); }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Detect_UnknownEcosystem_ReturnsNull()
    {
        var dir = RepoWith("README.md");
        try { Assert.Null(BuildCommandAutoDetector.Detect(dir.FullName)); }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Detect_NonExistentPath_ReturnsNull()
        => Assert.Null(BuildCommandAutoDetector.Detect(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid())));
}
