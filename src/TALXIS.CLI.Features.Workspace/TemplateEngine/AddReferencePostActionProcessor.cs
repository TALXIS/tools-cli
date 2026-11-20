using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Logging;


namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    public class AddReferencePostActionProcessor : IPostActionProcessor
    {
        private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AddReferencePostActionProcessor));
        public Guid ActionId => new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814");

        /// <summary>
        /// Why the last ProcessInternal call failed, or null when it succeeded.
        /// </summary>
        public string? LastError { get; private set; }

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            return ProcessInternal(environment, action, System.Environment.CurrentDirectory);
        }

        public bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, string outputBasePath)
        {
            LastError = null;
            var args = action.Args;
            if (!args.TryGetValue("projectFile", out var projectFile) || !args.TryGetValue("referenceFile", out var referenceFile))
            {
                return Fail("Add reference post-action missing required arguments");
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
                    return Fail("dotnet add reference timed out after 60 seconds");
                }
                if (process.ExitCode != 0)
                {
                    return Fail($"dotnet add reference exited with code {process.ExitCode}: {stderr.Trim()}");
                }
                return true;
            }
            catch (Exception ex)
            {
                return Fail($"Failed to add reference: {ex.Message}");
            }
        }

        private bool Fail(string error)
        {
            _logger.LogError("{Error}", error);
            LastError = error;
            return false;
        }
    }
}
