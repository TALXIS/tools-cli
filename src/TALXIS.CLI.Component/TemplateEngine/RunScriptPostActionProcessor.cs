using Microsoft.TemplateEngine.Abstractions;


namespace TALXIS.CLI.Component
{
    public class RunScriptPostActionProcessor : IPostActionProcessor
    {
        public Guid ActionId => new Guid("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            // Improved script execution logic with detailed logging
            var args = action.Args;
            if (!args.TryGetValue("executable", out var executable))
            {
                Console.Error.WriteLine("Script post-action missing 'executable' argument.");
                return false;
            }
            var scriptArgs = args.TryGetValue("args", out var scriptArgsValue) ? scriptArgsValue : string.Empty;
            // Log all post-action arguments for debugging
            Console.WriteLine("[RunScript] Action arguments:");
            foreach (var kv in args)
            {
                Console.WriteLine($"[RunScript]   {kv.Key} = {kv.Value}");
            }
            // Use 'targetDirectory' (should be the template output directory) if present, else fallback to current directory
            // Determine working directory: prefer 'workingDirectory', then 'targetDirectory', 'outputPath', 'destinationPath', else current directory
            string workingDir;
            if (args.TryGetValue("workingDirectory", out var wd) && !string.IsNullOrWhiteSpace(wd))
            {
                workingDir = wd;
            }
            else if (args.TryGetValue("targetDirectory", out var targetDir) && !string.IsNullOrWhiteSpace(targetDir))
            {
                workingDir = targetDir;
            }
            else if (args.TryGetValue("outputPath", out var outputPath) && !string.IsNullOrWhiteSpace(outputPath))
            {
                workingDir = outputPath;
            }
            else if (args.TryGetValue("destinationPath", out var destPath) && !string.IsNullOrWhiteSpace(destPath))
            {
                workingDir = destPath;
            }
            else
            {
                workingDir = Environment.CurrentDirectory;
            }
            try
            {
                Console.WriteLine($"[RunScript] Executing: {executable} {scriptArgs}");
                Console.WriteLine($"[RunScript] Working directory: {workingDir}");
                var process = new System.Diagnostics.Process
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
                process.Start();
                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(stdOut))
                {
                    Console.WriteLine($"[RunScript][stdout]:\n{stdOut}");
                }
                if (!string.IsNullOrWhiteSpace(stdErr))
                {
                    Console.Error.WriteLine($"[RunScript][stderr]:\n{stdErr}");
                }
                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"[RunScript] Script exited with code {process.ExitCode}.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RunScript] Failed to run script: {ex.Message}");
                return false;
            }
        }
    }
}
