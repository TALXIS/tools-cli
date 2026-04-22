using DotMake.CommandLine;

namespace TALXIS.CLI.Config.Commands.Auth;

/// <summary>
/// <c>txc config auth</c> — purpose-built credential verbs. Each kind
/// has its own verb (login / add-service-principal / add-federated)
/// instead of a shared <c>create --kind</c> surface: the options each
/// kind needs differ enough that purpose-built verbs stay simpler and
/// easier to document.
/// </summary>
[CliCommand(
    Name = "auth",
    Description = "Manage Entra / Dataverse credentials stored in the OS vault.",
    Children = new[]
    {
        typeof(AuthListCliCommand),
        typeof(AuthShowCliCommand),
        typeof(AuthDeleteCliCommand),
    }
)]
public class AuthCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
