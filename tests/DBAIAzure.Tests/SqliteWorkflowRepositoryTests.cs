// Integration tests for SqliteWorkflowRepository using a real SQLite :memory: database.
// Each test creates its own isolated connection so there is no shared state between tests.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Entities;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Integration tests for <see cref="SqliteWorkflowRepository"/> exercising each public
/// contract method against a real SQLite :memory: database. Using real SQLite rather than
/// the EF InMemory provider ensures that unique-index constraints are enforced, so the
/// name-conflict test cannot pass vacuously.
/// </summary>
public sealed class SqliteWorkflowRepositoryTests
{
    // ── Constant owner IDs used across tests ────────────────────────────────────────────────

    /// <summary>Primary owner identifier used for single-owner test scenarios.</summary>
    private const string OwnerA = "owner-a";

    /// <summary>Secondary owner identifier used to verify owner-scoping isolation.</summary>
    private const string OwnerB = "owner-b";

    // ── Helper ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh, isolated in-memory SQLite database, applies the full EF schema via
    /// <c>EnsureCreated</c>, and returns a repository backed by that database together with
    /// the open connection that keeps the database alive for the test's duration.
    /// The caller owns both the repository and the connection and must dispose the connection
    /// when the test completes to release the in-memory database.
    /// </summary>
    private static (SqliteWorkflowRepository repository, SqliteConnection keepAliveConnection) CreateRepository()
    {
        // Open the connection first — SQLite :memory: databases are destroyed the moment their
        // last connection closes, so this connection must stay open for the test's lifetime.
        var keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(keepAliveConnection)
            .Options;

        // Apply the schema exactly once using a short-lived seed context.
        using (var seedContext = new PipelineDbContext(options))
        {
            seedContext.Database.EnsureCreated();
        }

        var factory = new InMemoryDbContextFactory(options);
        var repository = new SqliteWorkflowRepository(factory);

        return (repository, keepAliveConnection);
    }

    // ── Tests ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that persisting a brand-new workflow definition assigns a non-empty Guid and
    /// returns it, confirming that the row was successfully written to the database.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NewWorkflow_ReturnsId()
    {
        var (repository, connection) = CreateRepository();
        await using (connection)
        {
            var workflow = WorkflowDefinition.CreateNew("My First Workflow", OwnerA);

            var returnedId = await repository.SaveAsync(workflow);

            Assert.NotEqual(Guid.Empty, returnedId);
        }
    }

    /// <summary>
    /// Verifies that all scalar and complex fields survive a round-trip through SQLite: the
    /// workflow is saved, retrieved by Id and owner, and every checked field matches the
    /// original. This confirms both the serialization and deserialization paths are correct.
    /// </summary>
    [Fact]
    public async Task GetAsync_ExistingWorkflow_RoundTripsAllFields()
    {
        var (repository, connection) = CreateRepository();
        await using (connection)
        {
            var settings = new WorkflowSettings { ExecutionTimeoutMinutes = 42 };
            var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step One");
            var workflow = WorkflowDefinition.CreateNew("Round-Trip Test", OwnerA) with
            {
                Nodes    = [node],
                Settings = settings,
            };

            await repository.SaveAsync(workflow);

            var retrieved = await repository.GetAsync(workflow.Id, OwnerA);

            Assert.NotNull(retrieved);
            Assert.Equal("Round-Trip Test", retrieved!.Name);
            Assert.Equal(OwnerA, retrieved.OwnerId);
            Assert.Single(retrieved.Nodes);
            Assert.Equal(42, retrieved.Settings.ExecutionTimeoutMinutes);
        }
    }

    /// <summary>
    /// Verifies owner-scoping: saving two workflows for ownerA and one for ownerB must cause
    /// <c>ListByOwnerAsync</c> to return exactly two results for ownerA and one for ownerB,
    /// confirming that the repository never leaks cross-owner data.
    /// </summary>
    [Fact]
    public async Task ListByOwnerAsync_ReturnsOnlyOwnerWorkflows()
    {
        var (repository, connection) = CreateRepository();
        await using (connection)
        {
            var workflowA1 = WorkflowDefinition.CreateNew("Workflow Alpha", OwnerA);
            var workflowA2 = WorkflowDefinition.CreateNew("Workflow Beta", OwnerA);
            var workflowB1 = WorkflowDefinition.CreateNew("Workflow Gamma", OwnerB);

            await repository.SaveAsync(workflowA1);
            await repository.SaveAsync(workflowA2);
            await repository.SaveAsync(workflowB1);

            var ownerAResults = await repository.ListByOwnerAsync(OwnerA);
            var ownerBResults = await repository.ListByOwnerAsync(OwnerB);

            Assert.Equal(2, ownerAResults.Count);
            Assert.Single(ownerBResults);
        }
    }

    /// <summary>
    /// Verifies that the unique <c>(OwnerId, Name)</c> index is enforced: saving a second
    /// workflow with the same owner and name but a different Id must throw
    /// <see cref="WorkflowNameConflictException"/> rather than silently overwriting the
    /// existing record. Using real SQLite ensures the constraint is genuinely exercised.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DuplicateName_ThrowsWorkflowNameConflictException()
    {
        var (repository, connection) = CreateRepository();
        await using (connection)
        {
            var firstWorkflow  = WorkflowDefinition.CreateNew("Duplicate Name", OwnerA);
            var secondWorkflow = WorkflowDefinition.CreateNew("Duplicate Name", OwnerA);

            await repository.SaveAsync(firstWorkflow);

            await Assert.ThrowsAsync<WorkflowNameConflictException>(
                () => repository.SaveAsync(secondWorkflow));
        }
    }

    /// <summary>
    /// Verifies that deleting a workflow by an Id that was never persisted returns
    /// <see langword="false"/>, keeping the delete operation idempotent-friendly for
    /// callers that may retry without knowing whether the record existed.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        var (repository, connection) = CreateRepository();
        await using (connection)
        {
            var nonExistentId = Guid.NewGuid();

            var wasDeleted = await repository.DeleteAsync(nonExistentId, OwnerA);

            Assert.False(wasDeleted);
        }
    }

    // ── Private helper type ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IDbContextFactory{TContext}"/> implementation that wraps a pre-built
    /// <see cref="DbContextOptions{TContext}"/> so all contexts created during a test share the
    /// same in-memory SQLite connection, preserving the database for the test's lifetime.
    /// </summary>
    private sealed class InMemoryDbContextFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        /// <summary>Creates a new <see cref="PipelineDbContext"/> bound to the shared connection.</summary>
        public PipelineDbContext CreateDbContext() => new(options);

        /// <summary>
        /// Asynchronous variant required by <see cref="IDbContextFactory{TContext}"/>; delegates
        /// to the synchronous path because in-memory context creation never performs I/O.
        /// </summary>
        public ValueTask<PipelineDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new PipelineDbContext(options));
    }
}
