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

            // Initialize telemetry from user config (fire-and-forget, never blocks)
            TxcTelemetryBootstrap.Initialize(entryPoint: "cli");

            try
            {
                return await Cli.RunAsync<TALXIS.CLI.TxcCliCommand>(args, new CliSettings { EnableDefaultExceptionHandler = true });
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
