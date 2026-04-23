using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Platform.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Builds a ready-to-use <see cref="DataverseConnection"/> from a resolved
/// (Profile, Connection, Credential) triple. Every leaf Dataverse command
/// goes through this one abstraction.
/// </summary>
public interface IDataverseConnectionFactory
{
    /// <summary>
    /// Connects to the Dataverse environment described by
    /// <paramref name="context"/>. Caller owns the returned
    /// <see cref="DataverseConnection"/> (dispose to release the
    /// <see cref="Microsoft.PowerPlatform.Dataverse.Client.ServiceClient"/>).
    /// </summary>
    Task<DataverseConnection> ConnectAsync(ResolvedProfileContext context, CancellationToken ct);
}
