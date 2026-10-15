using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// Shared base of the commands that act on one principal of a connection: exactly
/// one of <c>--user</c>, <c>--service-principal</c> and <c>--group</c>.
/// </summary>
public abstract class ConnectionPrincipalCommandBase : ConnectionCommandBase
{
    [CliOption(Name = "--user", Description = "User principal name or Entra object id.", Required = false)]
    public string? User { get; set; }

    [CliOption(Name = "--service-principal", Description = "Service principal application (client) id or Entra object id.", Required = false)]
    public string? ServicePrincipal { get; set; }

    [CliOption(Name = "--group", Description = "Entra group object id.", Required = false)]
    public string? Group { get; set; }

    /// <summary>The one principal the options name; logs and returns false unless exactly one is given.</summary>
    protected bool TryBuildPrincipal(out ConnectionPrincipalSpec principal)
    {
        var given = new (ConnectionPrincipalKind Kind, string? Identifier)[]
        {
            (ConnectionPrincipalKind.User, User),
            (ConnectionPrincipalKind.ServicePrincipal, ServicePrincipal),
            (ConnectionPrincipalKind.Group, Group),
        }.Where(p => !string.IsNullOrWhiteSpace(p.Identifier)).ToList();

        if (given.Count != 1)
        {
            Logger.LogError("Pass exactly one of --user, --service-principal or --group.");
            principal = null!;
            return false;
        }

        principal = new ConnectionPrincipalSpec(given[0].Kind, given[0].Identifier!);
        return true;
    }
}
