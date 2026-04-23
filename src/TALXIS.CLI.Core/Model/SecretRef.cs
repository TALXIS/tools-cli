using System.Text.Json.Serialization;

namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Opaque handle to a secret held in the OS vault. Never contains the secret value itself.
/// Canonical string form: <c>vault://com.talxis.txc/{credentialId}/{slot}</c>
/// where slot is one of <c>client-secret</c>, <c>pat</c>, <c>certificate-password</c>.
/// </summary>
public sealed record SecretRef
{
    public const string Scheme = "vault";
    public const string Service = "com.talxis.txc";

    public string CredentialId { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;

    public static SecretRef Create(string credentialId, string slot)
        => new() { CredentialId = credentialId, Slot = slot };

    /// <summary>Full canonical URI form.</summary>
    [JsonIgnore]
    public string Uri => $"{Scheme}://{Service}/{CredentialId}/{Slot}";

    public override string ToString() => Uri;

    public static SecretRef Parse(string value)
    {
        if (!TryParse(value, out var r))
            throw new FormatException($"Invalid SecretRef URI: '{value}'. Expected vault://{Service}/<credentialId>/<slot>.");
        return r!;
    }

    public static bool TryParse(string? value, out SecretRef? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, Service, StringComparison.OrdinalIgnoreCase)) return false;
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2) return false;
        result = new SecretRef { CredentialId = segments[0], Slot = segments[1] };
        return true;
    }
}
