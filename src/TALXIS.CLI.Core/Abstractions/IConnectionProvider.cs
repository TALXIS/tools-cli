using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>Depth of a <see cref="IConnectionProvider.ValidateAsync"/> check.</summary>
public enum ValidationMode
{
    /// <summary>Pure shape check — URLs, credential-kind compatibility, authority wiring.</summary>
    Structural,

    /// <summary>Structural, plus a live authenticated round-trip (e.g. WhoAmI).</summary>
    Live,
}

/// <summary>
/// Connects a Connection + Credential pair to a runtime service client.
/// Registered once per provider; the Dataverse implementation lives in
/// <c>TALXIS.CLI.Platform.Dataverse.Runtime</c>.
/// </summary>
public interface IConnectionProvider
{
    ProviderKind ProviderKind { get; }
    IReadOnlySet<CredentialKind> SupportedCredentialKinds { get; }

    /// <summary>Validate that the connection + credential can be used. Live mode issues a real request (e.g. WhoAmI for Dataverse).</summary>
    Task ValidateAsync(Connection connection, Credential credential, ValidationMode mode, CancellationToken ct);
}
