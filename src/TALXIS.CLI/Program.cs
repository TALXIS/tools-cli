using DotMake.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Providers.Dataverse.DependencyInjection;
using TALXIS.CLI.Environment.Platforms.Dataverse;

namespace TALXIS.CLI
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            int? packageDeployerExitCode = await PackageDeployerSubprocess.TryRunAsync(args);
            if (packageDeployerExitCode.HasValue)
            {
                return packageDeployerExitCode.Value;
            }

            // Bootstrap txc-config DI so every [CliCommand] handler can resolve
            // stores/resolvers/vault through TxcServices.Get<T>(). Done once per
            // process. The PackageDeployer subprocess branch short-circuits above
            // and wires its own TxcServices in the subprocess entry point.
            InitializeConfigServices();

            return await Cli.RunAsync<TALXIS.CLI.TxcCliCommand>(args, new CliSettings { EnableDefaultExceptionHandler = true });
        }

        public static async Task<int> RunCli(string[] args)
        {
            return await Main(args);
        }

        private static void InitializeConfigServices()
        {
            if (TxcServices.IsInitialized) return;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTxcConfigCore();
            services.AddTxcDataverseProvider();

            var provider = services.BuildServiceProvider();
            TxcServices.Initialize(provider);
        }
    }
}
