namespace TALXIS.CLI.Config.Model;

public sealed class ProfileCollection
{
    public List<Profile> Profiles { get; set; } = new();
}

public sealed class ConnectionCollection
{
    public List<Connection> Connections { get; set; } = new();
}

public sealed class CredentialCollection
{
    public List<Credential> Credentials { get; set; } = new();
}
