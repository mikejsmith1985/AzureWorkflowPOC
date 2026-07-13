// Execution-path guard (spec-019 T039 / SC-005): the migrated MAF executors and workflow factories must
// depend on the provider-neutral Microsoft.Extensions.AI IChatClient, never the retired SK
// IChatCompletionService. This is the reflection form of the "no LLM path depends on SK" grep gate.
using System.Reflection;
using DBAIAzure.Processes.Executors;
using Microsoft.Extensions.AI;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Asserts that no type in the MAF executor / workflow-factory namespaces takes a Semantic Kernel
/// chat-completion dependency, and that the model-using executors inject <see cref="IChatClient"/> — so the
/// MAF execution path is provably SK-free even while SK still backs the (pre-cutover) production default.
/// </summary>
public sealed class ExecutionPathSkFreeTests
{
    private static readonly string[] MafNamespaces =
    {
        "DBAIAzure.Processes.Executors",
        "DBAIAzure.Processes.Pipeline.Maf",
    };

    [Fact]
    public void MafExecutorsAndFactories_DoNotDependOnSkChatCompletion()
    {
        var assembly = typeof(IntakeExecutor).Assembly;

        var offenders = assembly.GetTypes()
            .Where(type => type.Namespace is not null && MafNamespaces.Any(ns => type.Namespace!.StartsWith(ns)))
            .SelectMany(type => type.GetConstructors())
            .SelectMany(ctor => ctor.GetParameters())
            .Where(parameter => IsSemanticKernelChat(parameter.ParameterType))
            .Select(parameter => parameter.ParameterType.FullName!)
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ModelUsingExecutors_InjectIChatClient()
    {
        foreach (var executorType in new[] { typeof(IntakeExecutor), typeof(ValidationExecutor), typeof(EstimationExecutor), typeof(GapAnalysisExecutor) })
        {
            var takesChatClient = executorType.GetConstructors()
                .SelectMany(ctor => ctor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IChatClient));

            Assert.True(takesChatClient, $"{executorType.Name} should inject IChatClient");
        }
    }

    private static bool IsSemanticKernelChat(Type type) =>
        type.Namespace?.Contains("SemanticKernel", StringComparison.Ordinal) == true
        || type.Name == "IChatCompletionService";
}
