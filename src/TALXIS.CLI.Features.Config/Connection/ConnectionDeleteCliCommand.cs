using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection delete &lt;name&gt;</c> — removes a
/// connection. By default the command fails with exit 3 when one or
/// more profiles reference it (surfacing the profile ids in the error),
/// so the user can decide to rebind or delete them first.
/// <c>--force-orphan-profiles</c> opts into the parity behaviour of
/// <c>config auth delete</c>: the connection is removed and the
/// referring profiles are left orphaned with a warning each.
/// </summary>
[McpToolAnnotations(DestructiveHint = true)]
[CliCommand(
    Name = "delete",
    Description = "Delete a connection. Fails if profiles reference it unless --force-orphan-profiles."
)]
public class ConnectionDeleteCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ConnectionDeleteCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Connection name.")]
    public required string Name { get; set; }

    [CliOption(Name = "--force-orphan-profiles", Description = "Delete even if profiles reference this connection; leaves them orphaned.", Required = false)]
    public bool ForceOrphanProfiles { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogError("Connection name must be provided.");
            return ExitError;
        }

        var connStore = TxcServices.Get<IConnectionStore>();
        var profileStore = TxcServices.Get<IProfileStore>();

        var existing = await connStore.GetAsync(Name, CancellationToken.None).ConfigureAwait(false);
        if (existing is null)
        {
            _logger.LogError("Connection '{Name}' not found.", Name);
            return ExitValidationError;
        }

        var profiles = await profileStore.ListAsync(CancellationToken.None).ConfigureAwait(false);
        var referencing = profiles
            .Where(p => string.Equals(p.ConnectionRef, Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (referencing.Count > 0 && !ForceOrphanProfiles)
        {
            _logger.LogError(
                "Connection '{Name}' is referenced by profile(s): {Profiles}. " +
                "Rebind or delete them first, or pass --force-orphan-profiles.",
                Name, string.Join(", ", referencing.Select(p => $"'{p.Id}'")));
            return 3;
        }

        foreach (var p in referencing)
        {
            _logger.LogWarning(
                "Profile '{ProfileId}' referenced connection '{Name}' and is now orphaned. " +
                "Update or delete the profile explicitly.",
                p.Id, Name);
        }

        var removed = await connStore.DeleteAsync(Name, CancellationToken.None).ConfigureAwait(false);
        if (!removed)
        {
            _logger.LogError("Connection '{Name}' disappeared during delete.", Name);
            return ExitError;
        }

        _logger.LogInformation("Connection '{Name}' deleted.", Name);
        OutputFormatter.WriteResult("succeeded", $"Connection '{Name}' deleted.");
        return ExitSuccess;
    }
}
