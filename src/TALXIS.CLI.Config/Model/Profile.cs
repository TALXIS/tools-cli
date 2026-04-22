namespace TALXIS.CLI.Config.Model;

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
}
