using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace.Localization;

[CliCommand(
    Name = "localization",
    Alias = "l10n",
    Description = "Manage localization of Power Platform solution projects (add languages, export/import translations).",
    Children = new[]
    {
        typeof(LocalizationAddCliCommand),
        typeof(LocalizationExportCliCommand),
        typeof(LocalizationImportCliCommand),
        typeof(LocalizationShowCliCommand)
    })]
public class LocalizationCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
