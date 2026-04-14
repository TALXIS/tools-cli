using DotMake.CommandLine;
using TALXIS.CLI.Environment;

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

            return await Cli.RunAsync<TALXIS.CLI.TxcCliCommand>(args, new CliSettings { EnableDefaultExceptionHandler = true });
        }

        public static async Task<int> RunCli(string[] args)
        {
            return await Main(args);
        }
    }
}
