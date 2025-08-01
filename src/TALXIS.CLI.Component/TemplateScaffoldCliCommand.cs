using DotMake.CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TALXIS.CLI.Component;

/// <summary>
/// CLI command to scaffold a component from a template, passing parameters.
/// </summary>
[CliCommand(
    Description = "Scaffolds a component from a template and passes parameters.")]
public class TemplateScaffoldCliCommand
{
    [CliArgument(Description = "Short name of the template.")]
    public string ShortName { get; set; } = string.Empty;

    [CliOption(Name = "output", Description = "Output path for the scaffolded component.")]
    public string OutputPath { get; set; } = string.Empty;

    [CliOption(Description = "Template parameters in the form key=value. Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    public async Task<int> RunAsync()
    {
        try
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Param)
            {
                var idx = p.IndexOf('=');
                if (idx <= 0 || idx == p.Length - 1)
                {
                    Console.Error.WriteLine($"Invalid parameter format: '{p}'. Use key=value.");
                    return 1;
                }
                var key = p.Substring(0, idx);
                var value = p.Substring(idx + 1);
                parameters[key] = value;
            }

            using var scaffolder = new TemplateInvoker();
            await scaffolder.ScaffoldAsync(ShortName, OutputPath, parameters);
            Console.WriteLine($"Component scaffolded to '{OutputPath}' using template '{ShortName}'.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error scaffolding component: {ex.Message}");
            return 1;
        }
        return 0;
    }
}
