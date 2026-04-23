using DotMake.CommandLine;
using TALXIS.CLI.Config.Providers.Dataverse.DependencyInjection;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;

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

            return await Cli.RunAsync<TALXIS.CLI.TxcCliCommand>(args, new CliSettings { EnableDefaultExceptionHandler = true });
        }

        public static async Task<int> RunCli(string[] args)
        {
            return await Main(args);
        }
    }
}
