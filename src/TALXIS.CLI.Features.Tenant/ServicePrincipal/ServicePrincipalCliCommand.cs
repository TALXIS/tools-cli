using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant.ServicePrincipal;

/// <summary>
/// Parent command for Entra application discovery and tenant-wide role assignment.
/// Usage: <c>txc tenant service-principal [list|get|role]</c>
/// </summary>
[CliCommand(
    Name = "service-principal",
    Description = "Discover Entra applications and manage their tenant-wide role assignments.",
    Children = new[]
    {
        typeof(ServicePrincipalListCliCommand),
        typeof(ServicePrincipalGetCliCommand),
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
/// Sub-resource for tenant-wide role assignments on an Entra application.
/// Usage: <c>txc tenant service-principal role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Manage tenant-wide role assignments for an Entra application.",
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
