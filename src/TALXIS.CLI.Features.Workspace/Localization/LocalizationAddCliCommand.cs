using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.Localization;

[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Register a language (LCID) in every Customizations.xml of the workspace so solutions declare support for it.")]
public class LocalizationAddCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LocalizationAddCliCommand));

    [CliOption(Name = "--language", Description = "Language to add (locale like cs-CZ or LCID like 1029).")]
    public required string Language { get; set; }

    [CliOption(Name = "--workspace", Description = "Workspace root (defaults to current directory).", Required = false)]
    public string? Workspace { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var root = Path.GetFullPath(Workspace ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(root))
        {
            Logger.LogError("Workspace not found: {Path}", root);
            return Task.FromResult(ExitValidationError);
        }

        var lcid = LanguageCodeResolver.Resolve(Language);
        var locale = LanguageCodeResolver.ToLocale(lcid);
        var result = LocalizationWriter.AddLanguageToCustomizations(root, lcid);

        var data = new
        {
            lcid,
            locale,
            filesTouched = result.FilesTouched,
            alreadyHad = result.Already,
        };
        OutputFormatter.WriteData(data, d =>
        {
            if (d.filesTouched == 0 && d.alreadyHad == 0)
            {
                OutputWriter.WriteLine($"No Customizations.xml under {root} — nothing to register. (If the language isn't yet declared at solution level, run `add` from the solution or repo root.)");
            }
            else
            {
                OutputWriter.WriteLine($"Registered LCID {d.lcid} ({d.locale}) in {d.filesTouched} Customizations.xml file(s); {d.alreadyHad} already had it.");
            }
        });

        return Task.FromResult(ExitSuccess);
    }
}
