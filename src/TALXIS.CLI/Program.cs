using DotMake.CommandLine;

namespace TALXIS.CLI
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await Cli.RunAsync<TALXIS.CLI.TxcCliCommand>(args);
        }

        public static async Task<int> RunCli(string[] args)
        {
            return await Main(args);
        }
    }
}
