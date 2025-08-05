using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Custom template engine host for TALXIS CLI that follows the same patterns as the official dotnet CLI.
    /// </summary>
    public class TalxisCliTemplateEngineHost : DefaultTemplateEngineHost
    {
        public TalxisCliTemplateEngineHost(
            string hostIdentifier,
            string version,
            Dictionary<string, string> preferences,
            IReadOnlyList<(Type InterfaceType, IIdentifiedComponent Instance)> builtIns,
            IReadOnlyList<string>? fallbackHostNames = null,
            string? outputPath = null,
            LogLevel logLevel = LogLevel.Information)
            : base(
                  hostIdentifier,
                  version,
                  preferences,
                  builtIns,
                  fallbackHostNames,
                  loggerFactory: Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
                    builder
                        .SetMinimumLevel(logLevel)
                        .AddConsole(options =>
                        {
                            options.FormatterName = ConsoleFormatterNames.Simple;
                        })))
        {
            string workingPath = FileSystem.GetCurrentDirectory();
            IsCustomOutputPath = outputPath != null;
            OutputPath = outputPath != null ? Path.Combine(workingPath, outputPath) : workingPath;
        }

        public string OutputPath { get; }

        public bool IsCustomOutputPath { get; }

        public override bool TryGetHostParamDefault(string paramName, out string? value)
        {
            // Add TALXIS-specific parameter defaults here
            switch (paramName.ToLowerInvariant())
            {
                case "allow-scripts":
                    value = "yes";
                    return true;
                default:
                    return base.TryGetHostParamDefault(paramName, out value);
            }
        }
    }
}
