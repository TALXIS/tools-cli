using System.Diagnostics;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Post-action processor for running PowerShell and other scripts during template processing.
    /// Follows the official .NET SDK ProcessStartPostActionProcessor pattern.
    /// </summary>
    public class RunScriptPostActionProcessor : IPostActionProcessor
    {
        internal static readonly Guid ActionProcessorId = new Guid("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

        public Guid ActionId => ActionProcessorId;

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            // Fall back to using Environment.CurrentDirectory if no explicit output path is provided
            return ProcessInternal(environment, action, null!, null!, Environment.CurrentDirectory);
        }

        public bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, ICreationEffects creationEffects, ICreationResult? templateCreationResult, string outputBasePath)
        {
            if (string.IsNullOrWhiteSpace(outputBasePath))
            {
                throw new ArgumentException($"'{nameof(outputBasePath)}' cannot be null or whitespace.", nameof(outputBasePath));
            }
            outputBasePath = Path.GetFullPath(outputBasePath);
            
            var args = action.Args;
            if (!args.TryGetValue("executable", out var executable))
            {
                Console.Error.WriteLine("[RunScript] Error: Script post-action missing 'executable' argument.");
                return false;
            }
            
            var scriptArgs = args.TryGetValue("args", out var scriptArgsValue) ? scriptArgsValue : string.Empty;
            
            // Use the explicit outputBasePath as working directory instead of Environment.CurrentDirectory
            // This ensures consistent behavior regardless of any directory changes by previous operations
            string workingDir = outputBasePath;
            
            // Check for explicit directory overrides
            if (args.TryGetValue("workingDirectory", out var wd) && !string.IsNullOrWhiteSpace(wd))
            {
                workingDir = Path.GetFullPath(wd);
            }

            try
            {
                Console.WriteLine($"[RunScript] Executing: {executable} {scriptArgs} in {workingDir}");
                
                // Resolve executable path like the official .NET SDK
                string resolvedExecutablePath = ResolveExecutableFilePath(environment.Host.FileSystem, executable, outputBasePath);
                
                var process = CreateProcess(resolvedExecutablePath, scriptArgs, workingDir);
                var (stdOut, stdErr, exitCode) = ExecuteProcess(process);
                
                LogProcessOutput(stdOut, stdErr, exitCode);
                
                return ValidateProcessResult(exitCode, stdErr);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RunScript] Error: Failed to run script - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolves the executable file path, checking if it exists in the working directory first.
        /// Based on the official .NET SDK ProcessStartPostActionProcessor implementation.
        /// </summary>
        private static string ResolveExecutableFilePath(IPhysicalFileSystem fileSystem, string executableFileName, string outputBasePath)
        {
            if (!string.IsNullOrEmpty(outputBasePath) && fileSystem.DirectoryExists(outputBasePath))
            {
                string executableCombinedFileName = Path.Combine(Path.GetFullPath(outputBasePath), executableFileName);
                if (fileSystem.FileExists(executableCombinedFileName))
                {
                    return executableCombinedFileName;
                }
            }

            // The executable has not been found in the template folder, thus do not use the full path to the file.
            // The executable will be further searched in the directories from the PATH environment variable.
            return executableFileName;
        }

        /// <summary>
        /// Creates and configures a process for script execution.
        /// </summary>
        private System.Diagnostics.Process CreateProcess(string executable, string scriptArgs, string workingDir)
        {
            return new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = scriptArgs,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
        }

        /// <summary>
        /// Executes a process and captures its output.
        /// </summary>
        private (string stdOut, string stdErr, int exitCode) ExecuteProcess(System.Diagnostics.Process process)
        {
            process.Start();
            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (stdOut, stdErr, process.ExitCode);
        }

        /// <summary>
        /// Logs process output with appropriate formatting.
        /// </summary>
        private void LogProcessOutput(string stdOut, string stdErr, int exitCode)
        {
            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                Console.WriteLine($"[RunScript][stdout]:\n{stdOut}");
            }
            
            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                Console.Error.WriteLine($"[RunScript][stderr]:\n{stdErr}");
            }
            
            Console.WriteLine($"[RunScript] Process exited with code: {exitCode}");
        }

        /// <summary>
        /// Validates process execution result and determines success/failure.
        /// </summary>
        private bool ValidateProcessResult(int exitCode, string stdErr)
        {
            if (exitCode != 0)
            {
                Console.Error.WriteLine($"[RunScript] Error: Script failed with exit code {exitCode}");
                return false;
            }

            // Check for PowerShell errors that may not set exit code
            if (HasCriticalErrors(stdErr))
            {
                Console.Error.WriteLine("[RunScript] Error: Script completed with exit code 0 but had critical errors");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if stderr contains critical errors that indicate script failure.
        /// </summary>
        private bool HasCriticalErrors(string stdErr)
        {
            if (string.IsNullOrWhiteSpace(stdErr))
                return false;

            string[] criticalErrors = {
                "Exception", "Error:", "cannot be loaded because running scripts is disabled",
                "cannot find path", "does not exist", "CommandNotFoundException",
                "Access is denied", "UnauthorizedAccessException", "DirectoryNotFoundException", "IOException"
            };

            return criticalErrors.Any(error => stdErr.Contains(error, StringComparison.OrdinalIgnoreCase));
        }
    }
}
