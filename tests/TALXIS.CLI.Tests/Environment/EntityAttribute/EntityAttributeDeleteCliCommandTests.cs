using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Environment.Entity;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.EntityAttribute;

[Collection("TxcServicesSerial")]
public sealed class EntityAttributeDeleteCliCommandTests
{
    private const string DependencyFault =
        "The Attribute(5f3c1f43-0000-0000-0000-000000000000) component cannot be deleted because it is referenced by 1 other components. " +
        "For a list of referenced components, use the RetrieveDependenciesForDeleteRequest.";

    [Fact]
    public async Task RunAsync_DeleteBlockedByDependencies_PointsToDependencyList()
    {
        var service = new FakeEntityMetadataService(new InvalidOperationException(
            "Delete failed.", new InvalidOperationException(DependencyFault)));
        using var host = new CommandTestHost(configureServices: s => s.AddSingleton<IDataverseEntityMetadataService>(service));
        await host.SeedActiveProfileAsync(EnvironmentType.Developer);

        var (exit, output) = await RunAsync();

        Assert.Equal(1, exit);
        var message = JsonDocument.Parse(output).RootElement.GetProperty("message").GetString();
        Assert.Contains("referenced by 1 other components", message);
        Assert.Contains("txc env component dependency list --entity tom_project --attribute tom_code", message);
    }

    [Fact]
    public async Task RunAsync_OtherFailure_HasNoDependencyHint()
    {
        var service = new FakeEntityMetadataService(new InvalidOperationException("Attribute 'tom_code' was not found."));
        using var host = new CommandTestHost(configureServices: s => s.AddSingleton<IDataverseEntityMetadataService>(service));
        await host.SeedActiveProfileAsync(EnvironmentType.Developer);

        var (exit, output) = await RunAsync();

        Assert.Equal(1, exit);
        Assert.DoesNotContain("dependency list", output);
    }

    [Fact]
    public async Task RunAsync_DeleteSucceeds_ReturnsZero()
    {
        var service = new FakeEntityMetadataService(failure: null);
        using var host = new CommandTestHost(configureServices: s => s.AddSingleton<IDataverseEntityMetadataService>(service));
        await host.SeedActiveProfileAsync(EnvironmentType.Developer);

        var (exit, _) = await RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal(("tom_project", "tom_code"), service.Deleted);
    }

    [Fact]
    public void IsBlockedByDependencies_MatchesOnlyTheDependencyFault()
    {
        Assert.True(AttributeDeleteDependencyHint.IsBlockedByDependencies(new Exception(DependencyFault)));
        Assert.True(AttributeDeleteDependencyHint.IsBlockedByDependencies(new Exception("outer", new Exception(DependencyFault))));
        Assert.False(AttributeDeleteDependencyHint.IsBlockedByDependencies(new Exception("Principal user is missing prvDeleteAttribute privilege.")));
    }

    private static async Task<(int Exit, string Output)> RunAsync()
    {
        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new EntityAttributeDeleteCliCommand
            {
                Format = "json",
                Entity = "tom_project",
                Name = "tom_code",
                Apply = true,
                Yes = true
            }.RunAsync();
        }

        return (exit, output.ToString());
    }

    private sealed class FakeEntityMetadataService(Exception? failure) : IDataverseEntityMetadataService
    {
        public (string Entity, string Attribute)? Deleted { get; private set; }

        public Task DeleteAttributeAsync(string? profileName, string entityLogicalName, string attributeLogicalName, CancellationToken ct)
        {
            if (failure is not null) throw failure;
            Deleted = (entityLogicalName, attributeLogicalName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EntitySummaryRecord>> ListEntitiesAsync(string? profileName, string? search, bool includeSystem, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<EntityAttributeRecord>> DescribeEntityAsync(string? profileName, string entityLogicalName, bool includeSystem, CancellationToken ct)
            => throw new NotImplementedException();

        public Task CreateAttributeAsync(string? profileName, CreateAttributeOptions options, CancellationToken ct)
            => throw new NotImplementedException();

        public Task CreateManyToManyRelationshipAsync(string? profileName, string entity1, string entity2, string schemaName, string? displayName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<EntityRelationshipRecord>> ListRelationshipsAsync(string? profileName, string entityLogicalName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task CreateEntityAsync(string? profileName, CreateEntityOptions options, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteEntityAsync(string? profileName, string entityLogicalName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task UpdateAttributeAsync(string? profileName, string entityLogicalName, string attributeLogicalName, string? displayName, string? description, string? requiredLevel, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<Dictionary<string, object?>> GetAttributeDetailAsync(string? profileName, string entityLogicalName, string attributeLogicalName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteRelationshipAsync(string? profileName, string relationshipSchemaName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<EntityDetailRecord> GetEntityDetailAsync(string? profileName, string entityLogicalName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task UpdateEntityAsync(string? profileName, string entityLogicalName, string? displayName, string? pluralName, string? description, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
