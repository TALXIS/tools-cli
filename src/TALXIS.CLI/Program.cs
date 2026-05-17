using DotMake.CommandLine;
using TALXIS.CLI.Core.Abstractions;
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

            // Initialize telemetry from user config (fire-and-forget, never blocks)
            InitializeTelemetry();

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

        private static void InitializeTelemetry()
        {
            try
            {
                var configStore = TxcServices.Get<IGlobalConfigStore>();
#pragma warning disable RS0030 // Synchronous telemetry init before async main loop — cannot use await here
                var config = configStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore RS0030
                TxcTelemetrySetup.Initialize(
                    configEnabled: config.Telemetry.Enabled,
                    configConnectionString: config.Telemetry.ConnectionString,
                    entryPoint: "cli");
            }
            catch (Exception)
            {
                // Telemetry initialization must never prevent CLI from running.
                // No logger available yet at this point in startup.
                return;
            }
        }
    }
}
