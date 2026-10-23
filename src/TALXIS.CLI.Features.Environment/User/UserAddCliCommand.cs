using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Provisions an Entra user into the environment so security roles can be
/// assigned immediately, without waiting for background JIT sync.
/// Usage: <c>txc environment user add --user &lt;upn-or-object-id&gt; [--role &lt;csv&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Grant a brand-new Entra user access to this environment. Use this before role add/self-elevate when the user has never signed in here and has no environment user record yet — a regular Dataverse user record cannot be created any other way, since it is otherwise only created by background sync the first time the user signs in. Use --role with one comma-separated value to assign initial roles in the same step. Safe to run again for a user who already has access. To grant yourself admin access when you don't yet have any access to this environment, use 'environment user self-elevate' instead."
)]
public class UserAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserAddCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object ID.", Required = true)]
    public string User { get; set; } = string.Empty;

    [CliOption(Name = "--role", Description = "Comma-separated role names or GUIDs, for example \"Basic User,Sales Manager\".", Required = false)]
    public string? Role { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!UserCliCommandSupport.TryParseRoleIdentifiers(Role, Logger, out var requestedRoles))
            return Task.FromResult(ExitValidationError);

        return ExecuteAddAsync(requestedRoles);
    }

    private async Task<int> ExecuteAddAsync(IReadOnlyList<string> requestedRoles)
    {
        try
        {
            var provisioning = TxcServices.Get<IEnvironmentUserProvisioningService>();
            var provisioned = await provisioning.ProvisionUserAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);

            // Roles are assigned by UPN/object-id lookup against the Dataverse
            // systemuser this call just ensured exists — prefer the resolved
            // UPN (matches Dataverse's domainname lookup) and fall back to the
            // original --user value (already accepted as GUID or UPN) if Graph
            // didn't return one.
            var userIdentifier = provisioned.UserPrincipalName ?? User;

            if (requestedRoles.Count == 0)
            {
                WriteAddResult(provisioned, Array.Empty<string>(), Array.Empty<UserRoleAssignmentFailure>());
                return ExitSuccess;
            }

            var service = TxcServices.Get<IDataverseUserService>();
            var assignedRoles = new List<string>(requestedRoles.Count);
            var failures = new List<UserRoleAssignmentFailure>();

            foreach (var role in requestedRoles)
                await TryAssignRoleAsync(service, userIdentifier, role, assignedRoles, failures).ConfigureAwait(false);

            WriteAddResult(provisioned, assignedRoles, failures);

            if (failures.Count == 0)
                return ExitSuccess;

            foreach (var failure in failures)
                Logger.LogError("Role '{Role}' was not assigned: {Error}", failure.Role, failure.Message);

            return failures.All(static failure => failure.IsValidationError)
                ? ExitValidationError
                : ExitError;
        }
        catch (Exception ex) when (UserCliCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task TryAssignRoleAsync(
        IDataverseUserService service,
        string userIdentifier,
        string role,
        ICollection<string> assignedRoles,
        ICollection<UserRoleAssignmentFailure> failures)
    {
        try
        {
            await service.AddRoleAsync(Profile, userIdentifier, role, CancellationToken.None).ConfigureAwait(false);
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
        IReadOnlyList<string> assignedRoles,
        IReadOnlyList<UserRoleAssignmentFailure> failures)
    {
        var payload = new
        {
            status = failures.Count == 0 ? "added" : "partial",
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
