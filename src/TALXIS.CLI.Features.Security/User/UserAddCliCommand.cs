using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Provisions an Entra user into a Dataverse environment so security roles can be assigned immediately.
/// Usage: <c>txc security user add --user &lt;upn-or-object-id&gt; [--role &lt;csv&gt;] [--environment &lt;id&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Grant a brand-new Entra user access to a Dataverse environment so role assignment commands can run immediately. This command requires --environment or an active environment connection because there is no tenant-wide equivalent. Use --role with a comma-separated list to assign initial Dataverse security roles in the same step."
)]
public class UserAddCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserAddCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object ID.", Required = true)]
    public string User { get; set; } = string.Empty;

    [CliOption(Name = "--role", Description = "Comma-separated Dataverse role names or GUIDs, for example \"Basic User,Sales Manager\".", Required = false)]
    public string? Role { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!SecurityPrincipalCommandSupport.TryParseRoleIdentifiers(Role, Logger, out var requestedRoles))
            return Task.FromResult(ExitValidationError);

        return ExecuteAddAsync(requestedRoles);
    }

    private async Task<int> ExecuteAddAsync(IReadOnlyList<string> requestedRoles)
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security user add", CancellationToken.None).ConfigureAwait(false);

        try
        {
            var provisioning = TxcServices.Get<IEnvironmentUserProvisioningService>();
            var provisioned = await provisioning.ProvisionUserAsync(Profile, User, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            var userIdentifier = provisioned.UserPrincipalName ?? User;

            if (requestedRoles.Count == 0)
            {
                WriteAddResult(provisioned, scope.EnvironmentId, Array.Empty<string>(), Array.Empty<UserRoleAssignmentFailure>());
                return ExitSuccess;
            }

            var service = TxcServices.Get<IDataverseUserService>();
            var assignedRoles = new List<string>(requestedRoles.Count);
            var failures = new List<UserRoleAssignmentFailure>();

            foreach (var role in requestedRoles)
                await TryAssignRoleAsync(service, scope.EnvironmentId, userIdentifier, role, assignedRoles, failures).ConfigureAwait(false);

            WriteAddResult(provisioned, scope.EnvironmentId, assignedRoles, failures);

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
        IDataverseUserService service,
        Guid? environmentId,
        string userIdentifier,
        string role,
        ICollection<string> assignedRoles,
        ICollection<UserRoleAssignmentFailure> failures)
    {
        try
        {
            await service.AddRoleAsync(Profile, userIdentifier, role, CancellationToken.None, environmentId).ConfigureAwait(false);
            assignedRoles.Add(role);
        }
        catch (Exception ex) when (ex is DataverseAmbiguousMatchException or ArgumentException or InvalidOperationException)
        {
            failures.Add(new UserRoleAssignmentFailure(role, ex.Message, IsValidationError: true));
        }
        catch (Exception ex)
        {
            failures.Add(new UserRoleAssignmentFailure(role, ex.Message, IsValidationError: false));
        }
    }

    private static void WriteAddResult(
        EnvironmentUserProvisionResult provisioned,
        Guid? environmentId,
        IReadOnlyList<string> assignedRoles,
        IReadOnlyList<UserRoleAssignmentFailure> failures)
    {
        var payload = new
        {
            status = failures.Count == 0 ? "added" : "partial",
            environmentId,
            aadObjectId = provisioned.AadObjectId,
            userPrincipalName = provisioned.UserPrincipalName,
            displayName = provisioned.DisplayName,
            assignedRoles,
            failedRoles = failures.Select(static failure => new
            {
                role = failure.Role,
                error = failure.Message,
            }).ToArray(),
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine(failures.Count == 0
                ? "User granted access to this environment."
                : "User granted access to this environment, but one or more role assignments failed.");
            OutputWriter.WriteLine($"Environment ID:  {environmentId?.ToString() ?? "-"}");
            OutputWriter.WriteLine($"Entra Object ID: {provisioned.AadObjectId}");
            OutputWriter.WriteLine($"UPN:             {provisioned.UserPrincipalName ?? "-"}");
            OutputWriter.WriteLine($"Display Name:    {provisioned.DisplayName ?? "-"}");

            if (assignedRoles.Count > 0)
            {
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Assigned roles ({assignedRoles.Count}):");
                foreach (var role in assignedRoles)
                    OutputWriter.WriteLine($"  - {role}");
            }

            if (failures.Count > 0)
            {
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Role assignment failures ({failures.Count}):");
                foreach (var failure in failures)
                    OutputWriter.WriteLine($"  - {failure.Role}: {failure.Message}");
            }
#pragma warning restore TXC003
        });
    }
}

internal sealed record UserRoleAssignmentFailure(string Role, string Message, bool IsValidationError);
