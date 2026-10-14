using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Deletes an environment group.
/// Usage: <c>txc governance environment-group delete &lt;environment-group&gt;</c>
/// </summary>
/// <remarks>
/// The service rejects deletion (<c>409 Conflict</c>) while the group still
/// has member environments or assigned policy rules. A future
/// <c>--force</c> option will auto-unassign policies and remove members
/// before retrying delete (tracked separately); for now this command
/// surfaces that conflict with a clear, actionable message instead of a
/// raw HTTP error.
/// </remarks>
[CliDestructive("Permanently deletes the environment group. Fails if the group still has member environments or assigned policy rules; this action does not delete the member environments themselves.")]
[CliCommand(
    Name = "delete",
    Description = "Delete an environment group. Fails with a clear error if the group still has member environments or assigned policy rules — remove those first (txc governance environment-group environment remove / txc governance policy-rule unassign)."
)]
public class EnvironmentGroupDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync() => ExecuteDeleteAsync();

    private async Task<int> ExecuteDeleteAsync()
    {
        try
        {
            var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
            var existing = await EnvironmentGroupCommandSupport
                .ResolveAsync(context.Connection, context.Credential, EnvironmentGroup, CancellationToken.None)
                .ConfigureAwait(false);

            var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();
            await client.DeleteAsync(context.Connection, context.Credential, existing.Id, CancellationToken.None).ConfigureAwait(false);

            OutputFormatter.WriteResult("deleted", id: existing.Id.ToString());
            return ExitSuccess;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("EnvironmentsInEnvironmentGroup", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogError(
                "Cannot delete environment group '{EnvironmentGroup}': it still has member environments. Remove them first with 'txc governance environment-group environment remove'.",
                EnvironmentGroup);
            return ExitValidationError;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("PolicyAssignedToEnvironmentGroup", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogError(
                "Cannot delete environment group '{EnvironmentGroup}': it still has assigned policy rules. Unassign them first with 'txc governance policy-rule unassign'.",
                EnvironmentGroup);
            return ExitValidationError;
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitValidationError;
        }
    }
}
