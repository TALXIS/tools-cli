
using System.Threading.Tasks;
using DotMake.CommandLine;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await Cli.RunAsync<TALXIS.CLI.TxcCliCommand>(args);
    }
}
