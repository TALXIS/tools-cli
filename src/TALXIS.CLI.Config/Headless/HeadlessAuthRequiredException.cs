using System.Text.Json;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Headless;

/// <summary>
/// Thrown when an interactive-only authentication flow is attempted in a
/// headless / CI context. Carries a deterministic user-facing remedy that
/// lists every permitted credential kind plus the exact env vars / profile
/// commands needed to re-run non-interactively.
/// </summary>
public sealed class HeadlessAuthRequiredException : Exception
{
    /// <summary>Credential kinds that are permitted when <see cref="IHeadlessDetector.IsHeadless"/> is true.</summary>
    public static IReadOnlySet<CredentialKind> PermittedHeadlessKinds { get; } =
        new HashSet<CredentialKind>
        {
            CredentialKind.ClientSecret,
            CredentialKind.ClientCertificate,
            CredentialKind.ManagedIdentity,
            CredentialKind.WorkloadIdentityFederation,
            CredentialKind.AzureCli,
            CredentialKind.Pat,
        };

    public CredentialKind AttemptedKind { get; }
    public string HeadlessReason { get; }

    public HeadlessAuthRequiredException(CredentialKind attemptedKind, string headlessReason)
        : base(BuildMessage(attemptedKind, headlessReason))
    {
        AttemptedKind = attemptedKind;
        HeadlessReason = headlessReason;
    }

    private static string BuildMessage(CredentialKind kind, string reason)
    {
        var permitted = string.Join(", ",
            PermittedHeadlessKinds
                .Select(ToKebab)
                .OrderBy(s => s, StringComparer.Ordinal));

        return
            $"Credential kind '{ToKebab(kind)}' requires an interactive TTY, " +
            $"but this process is running in headless mode ({reason}). " +
            $"Permitted headless kinds: {permitted}. " +
            "To run non-interactively, either create a headless-capable credential " +
            "(e.g. `txc config auth create --kind client-secret ...`) and bind it to the profile, " +
            "or supply the credential via environment variables " +
            "(AZURE_CLIENT_ID / AZURE_CLIENT_SECRET / AZURE_TENANT_ID for SPN, " +
            "AZURE_FEDERATED_TOKEN_FILE for workload-identity federation).";
    }

    private static string ToKebab(CredentialKind kind)
        => JsonNamingPolicy.KebabCaseLower.ConvertName(kind.ToString());
}
