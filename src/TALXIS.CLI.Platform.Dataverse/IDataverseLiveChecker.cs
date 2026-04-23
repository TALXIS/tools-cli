using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Platform.Dataverse;

/// <summary>
/// Provider-specific live-check seam consumed by
/// <see cref="DataverseConnectionProvider"/> when
/// <see cref="TALXIS.CLI.Config.Abstractions.ValidationMode.Live"/> is
/// requested. Default implementation wires through
/// <c>DataverseMsalClientFactory</c> + HTTP to issue a WhoAmI request;
/// tests inject a fake so they can assert command behaviour without
/// standing up MSAL / HTTP. Full default HTTP implementation lands with
/// the <c>refactor-dataverse-commands</c> milestone — until then the
/// default impl surfaces a clear "not implemented" error that the
/// <c>profile validate</c> command maps to exit 1.
/// </summary>
public interface IDataverseLiveChecker
{
    Task<DataverseLiveCheckResult> CheckAsync(Connection connection, Credential credential, CancellationToken ct);
}

/// <summary>Canonical payload returned from a successful WhoAmI call.</summary>
public sealed record DataverseLiveCheckResult(Guid UserId, Guid BusinessUnitId, Guid OrganizationId);
