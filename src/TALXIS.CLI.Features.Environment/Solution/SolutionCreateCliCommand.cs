using System.Text.RegularExpressions;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Creates a new unmanaged solution on the LIVE environment. Requires an active profile."
)]
public class SolutionCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionCreateCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "Friendly display name.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--publisher", Description = "Publisher unique name.", Required = true)]
    public string Publisher { get; set; } = null!;

    [CliOption(Name = "--version", Description = "Solution version (default: 1.0.0.0).", Required = false)]
    public string Version { get; set; } = "1.0.0.0";

    [CliOption(Name = "--description", Description = "Solution description.", Required = false)]
    public string? Description { get; set; }

    private static readonly Regex UniqueNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    protected override async Task<int> ExecuteAsync()
    {
        if (!UniqueNamePattern.IsMatch(Name))
        {
            Logger.LogError("Invalid solution name '{Name}'. Only [A-Z], [a-z], [0-9], or _ are allowed. Must start with a letter or _.", Name);
            return ExitValidationError;
        }

        if (!System.Version.TryParse(Version, out _))
        {
            Logger.LogError("Invalid version '{Version}'. Use format: major.minor.build.revision", Version);
            return ExitValidationError;
        }

        var options = new SolutionCreateOptions(Name, DisplayName, Publisher, Version, Description);
        var service = TxcServices.Get<ISolutionCreateService>();
        var outcome = await service.CreateAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(outcome, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Created solution '{outcome.UniqueName}' (id: {outcome.SolutionId}, version: {outcome.Version}).");
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }
}
