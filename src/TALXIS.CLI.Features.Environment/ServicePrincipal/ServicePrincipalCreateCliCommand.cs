using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Creates a Dataverse service principal directly in the environment.
/// Usage: <c>txc environment service-principal create --service-principal &lt;entra-client-id&gt; [--business-unit &lt;name-or-guid&gt;] [--role &lt;csv&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a Dataverse service principal from an existing Entra app registration. This creates only the environment-side service principal record, so the app registration itself must already exist. No prior environment-side registration step is required. Use --role with one comma-separated value to assign initial roles. If the user is created but one or more role assignments fail, txc reports the created user, lists the failed roles, and exits non-zero so you can retry just those role assignments."
)]
public class ServicePrincipalCreateCliCommand : ProfiledCliCommand
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
        if (!ServicePrincipalCommandSupport.TryParseRoleIdentifiers(Role, Logger, out var requestedRoles))
            return Task.FromResult(ExitValidationError);

        return ExecuteCreateAsync(requestedRoles);
    }

    private async Task<int> ExecuteCreateAsync(IReadOnlyList<string> requestedRoles)
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var app = await service.CreateAsync(
                Profile,
                new DataverseServicePrincipalCreateOptions(ServicePrincipal, BusinessUnit, Array.Empty<string>()),
                CancellationToken.None).ConfigureAwait(false);

            if (requestedRoles.Count == 0)
            {
                ServicePrincipalCommandSupport.WriteCreateResult(app, Array.Empty<string>(), Array.Empty<ServicePrincipalRoleAssignmentFailure>());
                return ExitSuccess;
            }

            var assignedRoles = new List<string>(requestedRoles.Count);
            var failures = new List<ServicePrincipalRoleAssignmentFailure>();

            foreach (var role in requestedRoles)
                await TryAssignRoleAsync(service, app, role, assignedRoles, failures).ConfigureAwait(false);

            ServicePrincipalCommandSupport.WriteCreateResult(app, assignedRoles, failures);

            if (failures.Count == 0)
                return ExitSuccess;

            foreach (var failure in failures)
                Logger.LogError("Role '{Role}' was not assigned: {Error}", failure.Role, failure.Message);

            return failures.All(static failure => failure.IsValidationError)
                ? ExitValidationError
                : ExitError;
        }
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task TryAssignRoleAsync(
        IDataverseServicePrincipalService service,
        DataverseServicePrincipalRecord app,
        string role,
        ICollection<string> assignedRoles,
        ICollection<ServicePrincipalRoleAssignmentFailure> failures)
    {
        try
        {
            await service.AddRoleAsync(Profile, app.Id.ToString(), role, CancellationToken.None).ConfigureAwait(false);
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
