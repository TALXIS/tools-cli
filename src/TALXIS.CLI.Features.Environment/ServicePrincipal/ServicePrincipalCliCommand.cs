using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Parent command for Dataverse service-principal operations.
/// Service principals are represented by <c>systemuser</c>
/// rows with an application client ID.
/// Usage: <c>txc environment service-principal [list|get|create|update|delete|role]</c>
/// </summary>
[CliCommand(
    Name = "service-principal",
    Description = "Manage Dataverse service principals in the current environment.",
    Children = new[]
    {
        typeof(ServicePrincipalListCliCommand),
        typeof(ServicePrincipalGetCliCommand),
        typeof(ServicePrincipalCreateCliCommand),
        typeof(ServicePrincipalUpdateCliCommand),
        typeof(ServicePrincipalDeleteCliCommand),
        typeof(ServicePrincipalRoleCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ServicePrincipalCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for Dataverse security-role assignments on an service principal.
/// Usage: <c>txc environment service-principal role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Manage Dataverse security roles assigned to an service principal.",
    Children = new[]
    {
        typeof(ServicePrincipalRoleListCliCommand),
        typeof(ServicePrincipalRoleAddCliCommand),
        typeof(ServicePrincipalRoleRemoveCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ServicePrincipalRoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
