using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Profile;

/// <summary>
/// <c>txc config profile pin [&lt;name&gt;]</c> — writes
/// <c>&lt;cwd&gt;/.txc/workspace.json</c> so the active profile is
/// automatically resolved when any <c>txc</c> command runs from this
/// tree (or any child directory). See plan §precedence — a workspace
/// pin beats the global active pointer but is still overridden by
/// <c>--profile</c> and <c>TXC_PROFILE</c>.
///
/// <para>
/// Without <c>&lt;name&gt;</c> pins the current global active profile
/// (so <c>select</c> then <c>pin</c> is the usual flow); otherwise pins
/// the named profile (must exist).
/// </para>
/// </summary>
[CliCommand(
    Name = "pin",
    Description = "Pin the active profile (or <name>) to <cwd>/.txc/workspace.json."
)]
public class ProfilePinCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfilePinCliCommand));

    [CliArgument(Description = "Profile name to pin. Defaults to the global active profile.", Required = false)]
    public string? Name { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var profileStore = TxcServices.Get<IProfileStore>();
            var globalConfig = TxcServices.Get<IGlobalConfigStore>();
            var env = TxcServices.Get<IEnvironmentReader>();

            string? target = Name;
            if (string.IsNullOrWhiteSpace(target))
            {
                var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                target = global.ActiveProfile;
                if (string.IsNullOrWhiteSpace(target))
                {
                    _logger.LogError("No active profile is set. Pass <name> or run 'txc config profile select <name>' first.");
                    return 2;
                }
            }

            var profile = await profileStore.GetAsync(target!, CancellationToken.None).ConfigureAwait(false);
            if (profile is null)
            {
                _logger.LogError("Profile '{Name}' not found.", target);
                return 2;
            }

            var cwd = env.GetCurrentDirectory();
            var workspaceDir = Path.Combine(cwd, WorkspaceDiscovery.DirectoryName);
            var workspaceFile = Path.Combine(workspaceDir, WorkspaceDiscovery.FileName);

            var config = new WorkspaceConfig { DefaultProfile = profile.Id };
            await WriteWorkspaceConfigAsync(workspaceFile, config, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Pinned profile '{Id}' to '{Path}'.", profile.Id, workspaceFile);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new { profile = profile.Id, path = workspaceFile },
                TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pin profile.");
            return 1;
        }
    }

    // WorkspaceConfig shape is trivial enough that inlining a write here is
    // cleaner than introducing a new store abstraction just for one field.
    private static async Task WriteWorkspaceConfigAsync(string path, WorkspaceConfig config, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, config, TxcJsonOptions.Default, CancellationToken.None).ConfigureAwait(false);
            await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }
}
