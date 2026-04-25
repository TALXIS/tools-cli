using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Core.Model;

/// <summary>
/// A named binding of one <see cref="Connection"/> to one <see cref="Credential"/>.
/// The only primitive users pass into leaf commands (via <c>--profile</c>).
/// </summary>
public sealed class Profile
{
    public string Id { get; set; } = string.Empty;
    public string ConnectionRef { get; set; } = string.Empty;
    public string CredentialRef { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>When the profile was first persisted.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the profile was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Captured but unprocessed fields (forward-compat).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
