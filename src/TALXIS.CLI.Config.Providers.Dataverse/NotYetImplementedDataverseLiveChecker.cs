using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Providers.Dataverse;

/// <summary>
/// Placeholder <see cref="IDataverseLiveChecker"/> impl that fails with
/// a precise message until the real HTTP + MSAL-silent-token path lands
/// in milestone <c>refactor-dataverse-commands</c>. Registered by
/// default so <c>profile validate --live</c> degrades predictably
/// (exit 1 with remedy) instead of silently reporting success.
/// </summary>
internal sealed class NotYetImplementedDataverseLiveChecker : IDataverseLiveChecker
{
    public Task<DataverseLiveCheckResult> CheckAsync(Connection connection, Credential credential, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Live WhoAmI is not yet implemented for Dataverse. " +
            "Run 'txc config profile validate --skip-live' for structural validation, " +
            "or wait for the Dataverse command refactor milestone that wires real token acquisition.");
    }
}
