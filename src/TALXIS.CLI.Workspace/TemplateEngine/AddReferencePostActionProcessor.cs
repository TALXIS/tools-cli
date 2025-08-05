using Microsoft.TemplateEngine.Abstractions;


namespace TALXIS.CLI.Workspace.TemplateEngine
{
    public class AddReferencePostActionProcessor : IPostActionProcessor
    {
        public Guid ActionId => new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814");

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            // Example: dotnet add <project> reference <reference>
            var args = action.Args;
            if (!args.TryGetValue("projectFile", out var projectFile) || !args.TryGetValue("referenceFile", out var referenceFile))
            {
                Console.Error.WriteLine("Add reference post-action missing required arguments.");
                return false;
            }
            try
            {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"add \"{projectFile}\" reference \"{referenceFile}\"",
                    WorkingDirectory = Environment.CurrentDirectory,
                    // If environment is not null and you want to use a custom path, update here
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
                process.Start();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"dotnet add reference exited with code {process.ExitCode}.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to add reference: {ex.Message}");
                return false;
            }
        }
    }
}
