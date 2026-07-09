using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Creates a Dataverse application user directly in the environment.
/// Usage: <c>txc environment app create --app &lt;entra-client-id&gt; [--business-unit &lt;name-or-guid&gt;] [--role &lt;csv&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a Dataverse application user from an existing Entra app registration. This creates only the environment-side application user record, so the app registration itself must already exist. No prior environment-side registration step is required. Use --role with one comma-separated value to assign initial roles. If the user is created but one or more role assignments fail, txc reports the created user, lists the failed roles, and exits non-zero so you can retry just those role assignments."
)]
public class AppCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppCreateCliCommand));

    [CliOption(Name = "--app", Description = "Entra application client ID GUID.", Required = true)]
    public Guid App { get; set; }

    [CliOption(Name = "--business-unit", Description = "Business unit name or GUID. Defaults to the current caller's business unit.", Required = false)]
    public string? BusinessUnit { get; set; }

    [CliOption(Name = "--role", Description = "Comma-separated role names or GUIDs, for example \"System Administrator,Sales Manager\".", Required = false)]
    public string? Role { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!AppCommandSupport.TryParseRoleIdentifiers(Role, Logger, out var requestedRoles))
            return Task.FromResult(ExitValidationError);

        return ExecuteCreateAsync(requestedRoles);
    }

    private async Task<int> ExecuteCreateAsync(IReadOnlyList<string> requestedRoles)
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();
            var app = await service.CreateAsync(
                Profile,
                new DataverseAppUserCreateOptions(App, BusinessUnit, Array.Empty<string>()),
                CancellationToken.None).ConfigureAwait(false);

            if (requestedRoles.Count == 0)
            {
                AppCommandSupport.WriteCreateResult(app, Array.Empty<string>(), Array.Empty<AppRoleAssignmentFailure>());
                return ExitSuccess;
            }

            var assignedRoles = new List<string>(requestedRoles.Count);
            var failures = new List<AppRoleAssignmentFailure>();

            foreach (var role in requestedRoles)
                await TryAssignRoleAsync(service, app, role, assignedRoles, failures).ConfigureAwait(false);

            AppCommandSupport.WriteCreateResult(app, assignedRoles, failures);

            if (failures.Count == 0)
                return ExitSuccess;

            foreach (var failure in failures)
                Logger.LogError("Role '{Role}' was not assigned: {Error}", failure.Role, failure.Message);

            return failures.All(static failure => failure.IsValidationError)
                ? ExitValidationError
                : ExitError;
        }
        catch (Exception ex) when (AppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task TryAssignRoleAsync(
        IDataverseAppUserService service,
        DataverseAppUserRecord app,
        string role,
        ICollection<string> assignedRoles,
        ICollection<AppRoleAssignmentFailure> failures)
    {
        try
        {
            await service.AddRoleAsync(Profile, app.Id.ToString(), role, CancellationToken.None).ConfigureAwait(false);
            assignedRoles.Add(role);
        }
        catch (Exception ex) when (ex is DataverseAmbiguousMatchException or ArgumentException or InvalidOperationException)
        {
            failures.Add(new AppRoleAssignmentFailure(role, ex.Message, IsValidationError: true));
        }
        catch (Exception ex)
        {
            failures.Add(new AppRoleAssignmentFailure(role, ex.Message, IsValidationError: false));
        }
    }
}
