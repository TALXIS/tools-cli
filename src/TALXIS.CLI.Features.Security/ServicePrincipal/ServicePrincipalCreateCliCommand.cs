using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

/// <summary>
/// Creates a Dataverse service principal directly in the resolved environment.
/// Usage: <c>txc security service-principal create --service-principal &lt;entra-client-id&gt; [--business-unit &lt;name-or-guid&gt;] [--role &lt;csv&gt;] [--environment &lt;id&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a Dataverse service principal from an existing Entra app registration. This command requires --environment or an active environment connection because there is no tenant-wide creation equivalent. Use --role with a comma-separated list to assign initial Dataverse security roles in the same step."
)]
public class ServicePrincipalCreateCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalCreateCliCommand));

    [CliOption(Name = "--service-principal", Description = "Entra application client ID GUID.", Required = true)]
    public Guid ServicePrincipal { get; set; }

    [CliOption(Name = "--business-unit", Description = "Business unit name or GUID. Defaults to the current caller's business unit.", Required = false)]
    public string? BusinessUnit { get; set; }

    [CliOption(Name = "--role", Description = "Comma-separated role names or GUIDs, for example \"System Administrator,Sales Manager\".", Required = false)]
    public string? Role { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!SecurityPrincipalCommandSupport.TryParseRoleIdentifiers(Role, Logger, out var requestedRoles))
            return Task.FromResult(ExitValidationError);

        return ExecuteCreateAsync(requestedRoles);
    }

    private async Task<int> ExecuteCreateAsync(IReadOnlyList<string> requestedRoles)
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security service-principal create", CancellationToken.None).ConfigureAwait(false);

        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var app = await service.CreateAsync(
                Profile,
                new DataverseServicePrincipalCreateOptions(ServicePrincipal, BusinessUnit, Array.Empty<string>()),
                CancellationToken.None,
                scope.EnvironmentId).ConfigureAwait(false);

            if (requestedRoles.Count == 0)
            {
                ServicePrincipalCommandSupport.WriteCreateResult(app, Array.Empty<string>(), Array.Empty<ServicePrincipalRoleAssignmentFailure>());
                return ExitSuccess;
            }

            var assignedRoles = new List<string>(requestedRoles.Count);
            var failures = new List<ServicePrincipalRoleAssignmentFailure>();

            foreach (var role in requestedRoles)
                await TryAssignRoleAsync(service, scope.EnvironmentId, app, role, assignedRoles, failures).ConfigureAwait(false);

            ServicePrincipalCommandSupport.WriteCreateResult(app, assignedRoles, failures);

            if (failures.Count == 0)
                return ExitSuccess;

            foreach (var failure in failures)
                Logger.LogError("Role '{Role}' was not assigned: {Error}", failure.Role, failure.Message);

            return failures.All(static failure => failure.IsValidationError)
                ? ExitValidationError
                : ExitError;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task TryAssignRoleAsync(
        IDataverseServicePrincipalService service,
        Guid? environmentId,
        DataverseServicePrincipalRecord app,
        string role,
        ICollection<string> assignedRoles,
        ICollection<ServicePrincipalRoleAssignmentFailure> failures)
    {
        try
        {
            await service.AddRoleAsync(Profile, app.Id.ToString(), role, CancellationToken.None, environmentId).ConfigureAwait(false);
            assignedRoles.Add(role);
        }
        catch (Exception ex) when (ex is DataverseAmbiguousMatchException or ArgumentException or InvalidOperationException)
        {
            failures.Add(new ServicePrincipalRoleAssignmentFailure(role, ex.Message, IsValidationError: true));
        }
        catch (Exception ex)
        {
            failures.Add(new ServicePrincipalRoleAssignmentFailure(role, ex.Message, IsValidationError: false));
        }
    }
}
