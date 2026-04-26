using System.Text.RegularExpressions;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Publisher;

[CliIdempotent]
[CliCommand(Name = "create", Description = "Create a new solution publisher.")]
public class PublisherCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PublisherCreateCliCommand));

    [CliArgument(Name = "name", Description = "Publisher unique name ([A-Za-z_][A-Za-z0-9_]*, no spaces).")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "Friendly display name.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--prefix", Description = "Customization prefix (2-8 alphanumeric chars, must start with a letter, cannot start with 'mscrm').", Required = true)]
    public string Prefix { get; set; } = null!;

    [CliOption(Name = "--option-value-prefix", Description = "Choice value prefix (integer 10000-99999).", Required = true)]
    public int OptionValuePrefix { get; set; }

    [CliOption(Name = "--description", Description = "Publisher description.", Required = false)]
    public string? Description { get; set; }

    // Unique name: [A-Za-z_] followed by [A-Za-z0-9_]*
    private static readonly Regex UniqueNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // Prefix: 2-8 alphanumeric, must start with letter, no underscores
    private static readonly Regex PrefixPattern = new(@"^[A-Za-z][A-Za-z0-9]{1,7}$", RegexOptions.Compiled);

    protected override async Task<int> ExecuteAsync()
    {
        // Validate unique name
        if (!UniqueNamePattern.IsMatch(Name))
        {
            Logger.LogError(
                "Invalid publisher unique name '{Name}'. Only [A-Z], [a-z], [0-9], or _ are allowed. Must start with a letter or _.",
                Name);
            return ExitValidationError;
        }

        // Validate prefix
        if (!PrefixPattern.IsMatch(Prefix))
        {
            Logger.LogError(
                "Invalid prefix '{Prefix}'. Must be 2-8 alphanumeric characters, start with a letter, no underscores.",
                Prefix);
            return ExitValidationError;
        }

        if (Prefix.StartsWith("mscrm", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogError("Prefix cannot start with 'mscrm'. Got '{Prefix}'.", Prefix);
            return ExitValidationError;
        }

        // Validate option value prefix
        if (OptionValuePrefix < 10_000 || OptionValuePrefix > 99_999)
        {
            Logger.LogError("Choice value prefix must be between 10,000 and 99,999. Got {Value}.", OptionValuePrefix);
            return ExitValidationError;
        }

        var options = new PublisherCreateOptions(Name, DisplayName, Prefix, OptionValuePrefix, Description);
        var service = TxcServices.Get<IPublisherService>();
        var id = await service.CreateAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { id, uniqueName = Name, prefix = Prefix, optionValuePrefix = OptionValuePrefix },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Created publisher '{Name}' (prefix: {Prefix}, id: {id}).");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
