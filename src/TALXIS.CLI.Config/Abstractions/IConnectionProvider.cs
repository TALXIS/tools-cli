using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Abstractions;

/// <summary>
/// Connects a Connection + Credential pair to a runtime service client.
/// Registered once per provider; the Dataverse implementation lives in
/// <c>TALXIS.CLI.Config.Providers.Dataverse</c>.
/// </summary>
public interface IConnectionProvider
{
    ProviderKind ProviderKind { get; }
    IReadOnlySet<CredentialKind> SupportedCredentialKinds { get; }

    /// <summary>Validate that the connection + credential can be used (e.g. WhoAmI for Dataverse).</summary>
    Task ValidateAsync(Connection connection, Credential credential, CancellationToken ct);
}
