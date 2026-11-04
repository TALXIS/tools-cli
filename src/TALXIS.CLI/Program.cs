using DotMake.CommandLine;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.Dataverse.Application.DependencyInjection;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;

namespace TALXIS.CLI
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            int? packageDeployerExitCode = await LegacyAssemblyHostSubprocess.TryRunAsync(args);
            if (packageDeployerExitCode.HasValue)
            {
                return packageDeployerExitCode.Value;
            }

            // Bootstrap txc-config DI so every [CliCommand] handler can resolve
            // stores/resolvers/vault through TxcServices.Get<T>(). Done once per
            // process. The PackageDeployer subprocess branch short-circuits above
            // and wires its own TxcServices via the same bootstrap helper.
            TxcServicesBootstrap.EnsureInitialized();

            // Wire DotMake's DI bridge so command classes can use constructor injection.
            // This uses the same IServiceProvider as TxcServices — both paths resolve
            // from the same container. Existing TxcServices.Get<T>() calls still work.
            if (TxcServices.Provider is not null)
                Cli.Ext.SetServiceProvider(TxcServices.Provider);

            // Initialize telemetry from user config (fire-and-forget, never blocks).
            // When invoked as an MCP subprocess, TXC_ENTRY_POINT=mcp is set by the
            // MCP server so all child spans correctly report the MCP entry point.
            var entryPoint = Environment.GetEnvironmentVariable("TXC_ENTRY_POINT") ?? "cli";
            TxcTelemetryBootstrap.Initialize(entryPoint: entryPoint);

            // Wire support escalation info into CLI error output
            TALXIS.CLI.Core.TxcLeafCommand.SupportInfoFormatter = Logging.TxcSupportInfo.FormatEscalation;
            TALXIS.CLI.Core.OutputFormatter.SupportInfoSessionId =
                Logging.TxcTelemetrySetup.SessionResolver?.SessionId;

            try
            {
                var parser = Cli.GetParser<TALXIS.CLI.TxcCliCommand>(new CliSettings { EnableDefaultExceptionHandler = true });
                var parsed = parser.Parse(args);
                if (!UsageErrorReporter.HasUsageErrors(parsed.ParseResult)) return await parser.RunAsync(args);

                // Text mode keeps the parser's own error output; only the exit code changes.
                if (!UsageErrorReporter.TryWriteJson(parsed.ParseResult, args)) await parser.RunAsync(args);
                return UsageErrorReporter.ExitCode;
            }
            finally
            {
                TxcTelemetrySetup.Shutdown();
            }
        }

        public static async Task<int> RunCli(string[] args)
        {
            return await Main(args);
        }
    }
}
