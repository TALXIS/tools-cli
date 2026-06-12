using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// <c>txc environment update</c> — updates properties of an existing Power
/// Platform environment. Only the supplied options are changed; omitted
/// properties are left as-is. This is a tenant-level admin operation: the
/// active profile supplies the credential and cloud.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Update properties of an existing Power Platform environment (name, type, access). Requires an active profile (used for admin identity and cloud)."
)]
public class EnvironmentUpdateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentUpdateCliCommand));

    [CliArgument(Name = "id", Description = "Environment id (GUID) of the environment to update.")]
    public Guid EnvironmentId { get; set; }

    [CliOption(Name = "--name", Aliases = ["-n"], Description = "New display name for the environment.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--type", Aliases = ["-t"], Description = "Convert the environment to a different type (e.g. Sandbox to Production).", Required = false)]
    public EnvironmentType? Type { get; set; }

    [CliOption(Name = "--security-group-id", Aliases = ["-sg"], Description = "Entra security group id that gates access. Pass an empty GUID (00000000-0000-0000-0000-000000000000) to remove the restriction.", Required = false)]
    public Guid? SecurityGroupId { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var options = new EnvironmentUpdateOptions
        {
            EnvironmentId = EnvironmentId,
            DisplayName = Name,
            EnvironmentType = Type,
            SecurityGroupId = SecurityGroupId,
        };

        var service = TxcServices.Get<IEnvironmentManagementService>();
        var result = await service.UpdateAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        Logger.LogInformation("Environment {EnvironmentId} updated.", result.EnvironmentId);

        var payload = new
        {
            environmentId = result.EnvironmentId,
            displayName = result.DisplayName,
            type = result.EnvironmentType?.ToString(),
            status = result.Status,
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Environment ID: {result.EnvironmentId}");
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
                OutputWriter.WriteLine($"Display Name: {result.DisplayName}");
            if (result.EnvironmentType is not null)
                OutputWriter.WriteLine($"Type: {result.EnvironmentType}");
            OutputWriter.WriteLine($"Status: {result.Status}");
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }
}
