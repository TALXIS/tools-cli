using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

/// <summary>
/// Parent command for Entra application discovery and Dataverse environment service-principal management.
/// Usage: <c>txc security service-principal [list|get|create|update|delete|role]</c>
/// </summary>
[CliCommand(
    Name = "service-principal",
    Description = "Discover Entra applications tenant-wide, or manage Dataverse environment service principals when --environment is provided or resolved from the active connection.",
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
/// Sub-resource for tenant-wide and Dataverse security-role assignments on a service principal.
/// Usage: <c>txc security service-principal role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "List, add, or remove tenant admin roles and Dataverse security roles for a service principal. With an environment scope, role list shows tenant admin roles and environment security roles in separate sections.",
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
