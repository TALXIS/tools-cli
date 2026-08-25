using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata.Serialization.Xml.Scaffolding;

namespace TALXIS.CLI.Features.Workspace;

/// <summary>
/// Applies a rendered component scaffold to the solution in one in-process transaction.
/// Component-agnostic: the component type selects the applier inside the platform metadata
/// library, and payload is passed as role=path files and name=value parameters. Replaces
/// template PowerShell post-action scripts during the template-to-metadata migration.
/// </summary>
[CliIdempotent]
[CliCommand(
    Description = "Apply a rendered component scaffold to the solution (used by template post-actions)",
    Name = "apply-scaffold")]
public class ComponentApplyScaffoldCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentApplyScaffoldCliCommand));

    [CliOption(Name = "--component-type", Description = "Component type matching the template's componentType tag (e.g. Attribute)", Required = true)]
    public string ComponentType { get; set; } = null!;

    [CliOption(Name = "--solution-root", Description = "Folder containing the unpacked solution files (Other/, Entities/, OptionSets/)", Required = true)]
    public string SolutionRoot { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Rendered file in role=path format (e.g. attribute=.template.temp/attribute.xml). Can be specified multiple times.")]
    public List<string> File { get; set; } = new();

    [CliOption(Name = "--param", Description = "Scalar parameter in name=value format (e.g. entity=udpp_warehouseitem). Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    protected override Task<int> ExecuteAsync()
    {
        var result = ComponentScaffold.Apply(new ComponentScaffoldRequest
        {
            ComponentType = ComponentType,
            SolutionRootPath = Path.GetFullPath(SolutionRoot),
            Files = ParsePairs(File, "--file").ToDictionary(p => p.Key, p => Path.GetFullPath(p.Value), StringComparer.OrdinalIgnoreCase),
            Parameters = ParsePairs(Param, "--param").ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
        });

        foreach (var warning in result.Warnings)
        {
            Logger.LogWarning("{Warning}", warning);
        }

        OutputFormatter.WriteResult("succeeded", $"{ComponentType} scaffold applied");
        return Task.FromResult(ExitSuccess);
    }

    private static IEnumerable<KeyValuePair<string, string>> ParsePairs(IEnumerable<string> pairs, string optionName)
    {
        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0 || idx == pair.Length - 1)
                throw new ArgumentException($"Invalid {optionName} format: '{pair}'. Use key=value.");
            yield return new KeyValuePair<string, string>(pair.Substring(0, idx), pair.Substring(idx + 1));
        }
    }
}
