using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Connection management over the connectivity API: one implementation for
/// every connector and every authentication method, for users and service
/// principals alike. Delegates to <see cref="ConnectionReader"/> and
/// <see cref="ConnectionProvisioner"/>.
/// </summary>
internal sealed class ConnectionService : IConnectionService
{
    private readonly ConnectionReader _reader;
    private readonly ConnectionProvisioner _provisioner;

    public ConnectionService(ConnectionReader reader, ConnectionProvisioner provisioner)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    }

    public Task<IReadOnlyList<ConnectionSummary>> ListAsync(
        string? profileName, string? connector, string? filter, CancellationToken ct)
        => _reader.ListAsync(profileName, connector, filter, ct);

    public Task<ConnectionSummary> GetAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
        => _reader.GetAsync(profileName, connector, connectionId, ct);

    public Task<ConnectionTestOutcome> TestAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
        => _reader.TestAsync(profileName, connector, connectionId, ct);

    public Task<ConnectionCreateOutcome> CreateAsync(
        string? profileName, ConnectionCreateOptions options, CancellationToken ct)
        => _provisioner.CreateAsync(profileName, options, ct);

    public Task DeleteAsync(string? profileName, string connector, string connectionId, CancellationToken ct)
        => _provisioner.DeleteAsync(profileName, connector, connectionId, ct);
}
