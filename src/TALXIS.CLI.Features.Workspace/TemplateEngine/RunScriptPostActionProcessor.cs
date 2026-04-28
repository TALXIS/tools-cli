using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    /// <summary>
    /// Post-action processor for running PowerShell and other scripts during template processing.
    /// Follows the official .NET SDK ProcessStartPostActionProcessor pattern.
    /// </summary>
    public class RunScriptPostActionProcessor : IPostActionProcessor
    {
        private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(RunScriptPostActionProcessor));
        internal static readonly Guid ActionProcessorId = new Guid("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

        public Guid ActionId => ActionProcessorId;

        /// <summary>
        /// Contains the error detail from the last failed script execution.
        /// Reset on each call to ProcessInternal/Process.
        /// </summary>
        public string? LastError { get; private set; }

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            // Fall back to using System.Environment.CurrentDirectory if no explicit output path is provided
            return ProcessInternal(environment, action, null!, null!, System.Environment.CurrentDirectory);
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
                _logger.LogError("[RunScript] Script post-action missing 'executable' argument");
                return false;
            }
            
            var scriptArgs = args.TryGetValue("args", out var scriptArgsValue) ? scriptArgsValue : string.Empty;
            
            // Use the explicit outputBasePath as working directory instead of System.Environment.CurrentDirectory
            // This ensures consistent behavior regardless of any directory changes by previous operations
            string workingDir = outputBasePath;
            
            // Check for explicit directory overrides
            if (args.TryGetValue("workingDirectory", out var wd) && !string.IsNullOrWhiteSpace(wd))
            {
                workingDir = Path.GetFullPath(wd);
            }

            LastError = null;
            try
            {
                _logger.LogInformation("[RunScript] Executing: {Executable} {Args} in {WorkDir}", executable, scriptArgs, workingDir);
                
                // Resolve executable path like the official .NET SDK
                string resolvedExecutablePath = ResolveExecutableFilePath(environment.Host.FileSystem, executable, outputBasePath);
                
                var process = CreateProcess(resolvedExecutablePath, scriptArgs, workingDir);
                var (stdOut, stdErr, exitCode) = ExecuteProcess(process);
                
                LogProcessOutput(stdOut, stdErr, exitCode);
                
                var success = ValidateProcessResult(exitCode, stdErr);
                if (!success)
                {
                    // Strip ANSI codes from the error detail so MCP clients see clean text
                    var cleanStdErr = !string.IsNullOrWhiteSpace(stdErr) 
                        ? System.Text.RegularExpressions.Regex.Replace(stdErr.Trim(), @"\x1B\[[0-9;]*m", "") 
                        : null;
                    LastError = cleanStdErr != null 
                        ? $"exit code {exitCode}: {cleanStdErr}" 
                        : $"exit code {exitCode}";
                }
                return success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.LogError("[RunScript] Failed to run '{Executable} {Args}' in {WorkDir}: {Message}", executable, scriptArgs, workingDir, ex.Message);
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
                _logger.LogInformation("[RunScript][stdout]:\n{Output}", stdOut);
            }
            
            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                _logger.LogWarning("[RunScript][stderr]:\n{Output}", stdErr);
            }
            
            _logger.LogInformation("[RunScript] Process exited with code: {ExitCode}", exitCode);
        }

        /// <summary>
        /// Validates process execution result and determines success/failure.
        /// </summary>
        private bool ValidateProcessResult(int exitCode, string stdErr)
        {
            if (exitCode != 0)
            {
                var detail = !string.IsNullOrWhiteSpace(stdErr) ? $"\n{stdErr.Trim()}" : "";
                _logger.LogError("[RunScript] Script failed with exit code {ExitCode}{Detail}", exitCode, detail);
                return false;
            }

            // Check for PowerShell errors that may not set exit code
            if (HasCriticalErrors(stdErr))
            {
                _logger.LogError("[RunScript] Script completed with exit code 0 but stderr contains errors:\n{StdErr}", stdErr.Trim());
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

            // Strip ANSI escape codes — PowerShell writes colored error output to stderr
            // which can break keyword matching (e.g., "\x1B[31;1mMove-Item:\x1B[0m" instead of "Move-Item:")
            var cleanStdErr = System.Text.RegularExpressions.Regex.Replace(stdErr, @"\x1B\[[0-9;]*m", "");

            string[] criticalErrors = {
                "Exception", "Error:", "cannot be loaded because running scripts is disabled",
                "cannot find path", "does not exist", "CommandNotFoundException",
                "Access is denied", "UnauthorizedAccessException", "DirectoryNotFoundException", "IOException"
            };

            return criticalErrors.Any(error => cleanStdErr.Contains(error, StringComparison.OrdinalIgnoreCase));
        }
    }
}
