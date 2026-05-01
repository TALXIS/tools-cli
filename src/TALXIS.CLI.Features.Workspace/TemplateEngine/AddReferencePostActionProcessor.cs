using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Logging;


namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    public class AddReferencePostActionProcessor : IPostActionProcessor
    {
        private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AddReferencePostActionProcessor));
        public Guid ActionId => new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814");

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            return ProcessInternal(environment, action, System.Environment.CurrentDirectory);
        }

        public bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, string outputBasePath)
        {
            var args = action.Args;
            if (!args.TryGetValue("projectFile", out var projectFile) || !args.TryGetValue("referenceFile", out var referenceFile))
            {
                _logger.LogError("Add reference post-action missing required arguments");
                return false;
            }
            try
            {
                var workingDir = !string.IsNullOrWhiteSpace(outputBasePath) ? outputBasePath : System.Environment.CurrentDirectory;
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"add \"{projectFile}\" reference \"{referenceFile}\"",
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(60_000))
                {
                    process.Kill();
                    _logger.LogError("dotnet add reference timed out after 60 seconds");
                    return false;
                }
                if (process.ExitCode != 0)
                {
                    _logger.LogError("dotnet add reference exited with code {ExitCode}", process.ExitCode);
                    if (!string.IsNullOrWhiteSpace(stderr))
                        _logger.LogError("stderr: {Output}", stderr);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to add reference: {Message}", ex.Message);
                return false;
            }
        }
    }
}
