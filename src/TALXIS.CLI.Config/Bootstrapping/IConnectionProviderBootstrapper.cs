using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Bootstrapping;

/// <summary>
/// Inputs to <see cref="IConnectionProviderBootstrapper.BootstrapAsync"/>.
/// Captures everything needed to stand up a credential + connection pair
/// in one shot. The bootstrapper owns interactive login (if any) and the
/// upsert of both <see cref="Credential"/> and <see cref="Connection"/>.
/// </summary>
public sealed record ProfileBootstrapRequest(
    string Name,
    ProviderKind Provider,
    string EnvironmentUrl,
    CloudInstance Cloud,
    string? TenantId,
    string? Description);

/// <summary>
/// Outcome of a bootstrap attempt. <see cref="Error"/> is populated on
/// validation / non-exceptional failure so the caller can translate into
/// its chosen exit code. Unexpected failures still throw.
/// </summary>
public sealed record ProfileBootstrapResult(
    Credential? Credential,
    Connection? Connection,
    string? Upn,
    string? Error);

/// <summary>
/// Provider-scoped orchestrator for the <c>profile create --url</c>
/// one-liner. Composes interactive login + alias resolution + credential
/// upsert + connection upsert into a single step so that there is exactly
/// one path that writes these primitives from a URL (the primitive
/// commands delegate to the same helpers — no duplicated rules).
/// </summary>
public interface IConnectionProviderBootstrapper
{
    ProviderKind Provider { get; }

    Task<ProfileBootstrapResult> BootstrapAsync(
        ProfileBootstrapRequest request, CancellationToken ct);
}
