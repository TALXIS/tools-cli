using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;

/// <summary>
/// Per-test <c>TxcServices</c> scope with scriptable stand-ins for the connection,
/// permission, connection-reference and connector services, and for the process
/// environment. Commands resolve their services through <c>TxcServices</c>, which is
/// process-wide: tests using this host belong to the <c>TxcServicesSerial</c> collection.
/// </summary>
internal sealed class ConnectionCommandTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public FakeConnections Connections { get; } = new();
    public FakePermissions Permissions { get; } = new();
    public FakeReferences References { get; } = new();
    public FakeConnectors Connectors { get; } = new();
    public FakeOperations Operations { get; } = new();
    public Dictionary<string, string> Environment { get; } = new();

    /// <param name="headless">True makes a destructive command without <c>--yes</c> refuse to run.</param>
    public ConnectionCommandTestHost(bool headless = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionService>(Connections);
        services.AddSingleton<IConnectionPermissionService>(Permissions);
        services.AddSingleton<IConnectionReferenceService>(References);
        services.AddSingleton<IConnectorService>(Connectors);
        services.AddSingleton<IPowerAutomateConnectorService>(Operations);
        services.AddSingleton<IEnvironmentReader>(new FakeEnvironment(Environment));
        services.AddSingleton<IHeadlessDetector>(new FakeHeadless(headless));
        _provider = services.BuildServiceProvider();
        TxcServices.Initialize(_provider);
    }

    /// <summary>Runs the command with JSON output and returns its exit code and stdout.</summary>
    public static async Task<(int Exit, string Output)> RunAsync(TxcLeafCommand command)
    {
        command.Format = "json";
        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
            exit = await command.RunAsync();

        return (exit, output.ToString());
    }

    public void Dispose()
    {
        TxcServices.Reset();
        _provider.Dispose();
    }

    private sealed class FakeEnvironment(Dictionary<string, string> variables) : IEnvironmentReader
    {
        public string? Get(string name) => variables.TryGetValue(name, out var value) ? value : null;
        public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
    }

    private sealed class FakeHeadless(bool isHeadless) : IHeadlessDetector
    {
        public bool IsHeadless { get; } = isHeadless;
        public string? Reason { get; } = isHeadless ? "test harness" : null;
    }
}

internal sealed class FakeConnections : IConnectionService
{
    public ConnectionCreateOutcome CreateOutcome { get; set; } =
        new(ConnectionOutcomeStatus.Created, "c1", "shared_office365users", "Mail", "Connected", null, null);

    public ConnectionTestOutcome TestOutcome { get; set; } = new(true, "Connected", null);

    public ConnectionSummary Summary { get; set; } =
        new("c1", "shared_office365users", "Mail", "Connected", null, "User", "admin@contoso.com", null);

    /// <summary>Thrown by every call when set.</summary>
    public Exception? Failure { get; set; }

    public ConnectionCreateOptions? CreatedWith { get; private set; }
    public string? LastConnector { get; private set; } = "<not called>";
    public string? LastFilter { get; private set; }
    public bool Deleted { get; private set; }

    public Task<IReadOnlyList<ConnectionSummary>> ListAsync(string? profileName, string? connector, string? filter, CancellationToken ct)
    {
        Fail();
        (LastConnector, LastFilter) = (connector, filter);
        return Task.FromResult<IReadOnlyList<ConnectionSummary>>([Summary]);
    }

    public Task<ConnectionSummary> GetAsync(string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        Fail();
        LastConnector = connector;
        return Task.FromResult(Summary);
    }

    public Task<ConnectionCreateOutcome> CreateAsync(string? profileName, ConnectionCreateOptions options, CancellationToken ct)
    {
        Fail();
        CreatedWith = options;
        return Task.FromResult(CreateOutcome);
    }

    public Task<ConnectionTestOutcome> TestAsync(string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        Fail();
        LastConnector = connector;
        return Task.FromResult(TestOutcome);
    }

    public Task DeleteAsync(string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        Fail();
        (LastConnector, Deleted) = (connector, true);
        return Task.CompletedTask;
    }

    private void Fail()
    {
        if (Failure is not null)
            throw Failure;
    }
}

internal sealed class FakePermissions : IConnectionPermissionService
{
    public ConnectionPermission Permission { get; set; } =
        new("p1", "obj-1", "User", "Alice", "alice@contoso.com", ConnectionPermissionRole.CanView);

    public ConnectionOutcomeStatus AddStatus { get; set; } = ConnectionOutcomeStatus.Assigned;

    public string? LastConnector { get; private set; } = "<not called>";
    public ConnectionPrincipalSpec? LastPrincipal { get; private set; }
    public ConnectionPermissionRole? LastRole { get; private set; }
    public bool Removed { get; private set; }

    public Task<IReadOnlyList<ConnectionPermission>> ListPermissionsAsync(string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        LastConnector = connector;
        return Task.FromResult<IReadOnlyList<ConnectionPermission>>([Permission]);
    }

    public Task<ConnectionPermissionOutcome> AddPermissionAsync(
        string? profileName, string? connector, string connectionId, ConnectionPrincipalSpec principal, ConnectionPermissionRole role, CancellationToken ct)
    {
        (LastConnector, LastPrincipal, LastRole) = (connector, principal, role);
        return Task.FromResult(new ConnectionPermissionOutcome(AddStatus, connectionId, "shared_office365users", Permission with { Role = role }));
    }

    public Task<ConnectionPermissionOutcome> RemovePermissionAsync(
        string? profileName, string? connector, string connectionId, ConnectionPrincipalSpec principal, CancellationToken ct)
    {
        (LastConnector, LastPrincipal, Removed) = (connector, principal, true);
        return Task.FromResult(new ConnectionPermissionOutcome(ConnectionOutcomeStatus.Removed, connectionId, "shared_office365users", Permission));
    }
}

internal sealed class FakeReferences : IConnectionReferenceService
{
    public static readonly Guid ReferenceId = new("5b9a1f3e-77a4-4a0e-9a5e-2f0c7b1d9c11");

    public ConnectionReferenceRecord Record { get; set; } = new(
        ReferenceId, "new_mail", "Mail", "shared_office365users", "c1", ConnectionBindingStatus.Bound, null, false, null);

    public Task<IReadOnlyList<ConnectionReferenceRecord>> ListAsync(string? profileName, ConnectionReferenceListOptions options, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConnectionReferenceRecord>>([Record]);

    public Task<ConnectionReferenceRecord> GetAsync(string? profileName, string logicalName, CancellationToken ct)
        => Task.FromResult(Record);

    public Task<ConnectionReferenceOutcome> CreateAsync(string? profileName, ConnectionReferenceCreateOptions options, CancellationToken ct)
        => Task.FromResult(new ConnectionReferenceOutcome(ConnectionOutcomeStatus.Created, Record));

    public Task<ConnectionReferenceOutcome> UpdateAsync(string? profileName, string logicalName, string? connectionId, string? displayName, CancellationToken ct)
        => Task.FromResult(new ConnectionReferenceOutcome(ConnectionOutcomeStatus.Updated, Record));

    public bool Deleted { get; private set; }

    public Task DeleteAsync(string? profileName, string logicalName, CancellationToken ct)
    {
        Deleted = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeConnectors : IConnectorService
{
    public bool Listed { get; private set; }
    public string? LastKind { get; private set; }

    public Task<IReadOnlyList<ConnectorListItem>> ListAsync(string? profileName, string? filter, string? kind, int? top, CancellationToken ct)
    {
        (Listed, LastKind) = (true, kind);
        return Task.FromResult<IReadOnlyList<ConnectorListItem>>([]);
    }
}

/// <summary>Records operation searches; the other operations are not exercised by command tests.</summary>
internal sealed class FakeOperations : IPowerAutomateConnectorService
{
    public bool Searched { get; private set; }
    public string? LastKind { get; private set; }

    public Task<IReadOnlyList<OperationSearchHit>> SearchOperationsAsync(
        string? profileName, string? query, string? connector, string? kind, int? top, CancellationToken ct)
    {
        (Searched, LastKind) = (true, kind);
        return Task.FromResult<IReadOnlyList<OperationSearchHit>>([]);
    }

    public Task<ConnectorDetail> GetConnectorAsync(string? profileName, string connector, string? query, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<OperationDetail> GetOperationDetailsAsync(string? profileName, string connector, string operation, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<FlowConnectionSummary>> ListConnectionsAsync(string? profileName, string? connector, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<FlowValidationReport> ValidateDefinitionAsync(
        string? profileName, JsonElement document, bool connectionCheck, bool offline, CancellationToken ct)
        => throw new NotSupportedException();
}
