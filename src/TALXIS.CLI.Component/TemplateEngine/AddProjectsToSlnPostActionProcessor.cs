using System;
using Microsoft.TemplateEngine.Abstractions;


namespace TALXIS.CLI.Component
{
    public class AddProjectsToSlnPostActionProcessor : IPostActionProcessor
    {
        public Guid ActionId => new Guid("D396686C-DE0E-4DE6-906D-291CD29FC5DE");

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            // Example: dotnet sln <slnFile> add <projectFiles>
            var args = action.Args;
            if (!args.TryGetValue("slnFile", out var slnFile) || !args.TryGetValue("projectFiles", out var projectFiles))
            {
                Console.Error.WriteLine("Add projects to .sln post-action missing required arguments.");
                return false;
            }
            try
            {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"sln \"{slnFile}\" add {projectFiles}",
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
                    Console.Error.WriteLine($"dotnet sln add exited with code {process.ExitCode}.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to add projects to .sln: {ex.Message}");
                return false;
            }
        }
    }
}
