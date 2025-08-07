using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Post-action processor that adds project files to a solution file.
    /// Supports both explicit file paths and auto-discovery from template outputs.
    /// </summary>
    public class AddProjectsToSlnPostActionProcessor : IPostActionProcessor
    {
        public Guid ActionId => new Guid("D396686C-DE0E-4DE6-906D-291CD29FC5DE");

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            // Fall back to using Environment.CurrentDirectory if no explicit output path is provided
            return ProcessInternal(environment, action, null!, null!, Environment.CurrentDirectory);
        }

        /// <summary>
        /// Processes the post-action to add projects to a solution file.
        /// </summary>
        public bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, ICreationEffects creationEffects, ICreationResult? templateCreationResult, string outputBasePath)
        {
            Console.WriteLine($"[AddProjectsToSln] Processing with output path: {outputBasePath}");
            
            if (templateCreationResult == null)
            {
                Console.Error.WriteLine("❌ templateCreationResult is null - cannot process primary outputs");
                return false;
            }
            
            try
            {
                // Follow the official .NET SDK pattern - use primary outputs from templateCreationResult directly
                var (slnFile, projectFiles) = GetFilesToProcessFromSdk(action, templateCreationResult, outputBasePath);
                
                if (string.IsNullOrEmpty(slnFile) || !projectFiles.Any())
                {
                    Console.Error.WriteLine("❌ No solution file or project files found to process.");
                    return false;
                }

                // Add projects to solution using dotnet CLI
                return AddProjectsToSolution(slnFile, projectFiles, outputBasePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Failed to add projects to solution: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Gets the solution file and project files to process based on action arguments.
        /// Follows the official .NET SDK DotnetSlnPostActionProcessor pattern.
        /// </summary>
        private (string? slnFile, IEnumerable<string> projectFiles) GetFilesToProcessFromSdk(IPostAction action, ICreationResult? templateCreationResult, string outputBasePath)
        {
            // Find solution file - matches official SDK FindSolutionFilesAtOrAbovePath
            var foundSlnFile = FindSolutionFile(outputBasePath);
            if (foundSlnFile == null)
            {
                Console.Error.WriteLine("❌ No solution file found.");
                return (null, Enumerable.Empty<string>());
            }

            // Get project files - matches official SDK TryGetProjectFilesToAdd behavior
            var projectFiles = GetProjectFilesFromPrimaryOutputs(action, templateCreationResult, outputBasePath);
            if (!projectFiles.Any())
            {
                Console.Error.WriteLine("❌ No project files found to add.");
                return (null, Enumerable.Empty<string>());
            }

            // Validate inRoot vs solutionFolder conflict (like official implementation)
            if (action.Args?.TryGetValue("inRoot", out var inRootValue) == true && 
                bool.TryParse(inRootValue, out var inRoot) && inRoot &&
                action.Args.TryGetValue("solutionFolder", out var solutionFolder) &&
                !string.IsNullOrWhiteSpace(solutionFolder))
            {
                Console.Error.WriteLine("❌ Cannot specify both inRoot=true and solutionFolder.");
                return (null, Enumerable.Empty<string>());
            }

            Console.WriteLine($"✅ Found solution and {projectFiles.Count()} project file(s) to add");
            return (foundSlnFile, projectFiles);
        }

        /// <summary>
        /// Gets project files from primary outputs following official SDK pattern.
        /// </summary>
        private static IEnumerable<string> GetProjectFilesFromPrimaryOutputs(IPostAction action, ICreationResult? templateCreationResult, string outputBasePath)
        {
            var primaryOutputs = templateCreationResult?.PrimaryOutputs;
            
            if (primaryOutputs == null || !primaryOutputs.Any())
            {
                return Enumerable.Empty<string>();
            }

            // Use specific indexes if provided (matches official SDK behavior)
            if (action.Args?.TryGetValue("primaryOutputIndexes", out var indexesArg) == true && !string.IsNullOrEmpty(indexesArg))
            {
                var indexes = indexesArg.Split([';'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(s => int.Parse(s))
                    .Where(i => i >= 0 && i < primaryOutputs.Count)
                    .ToList();

                if (indexes.Any())
                {
                    return indexes.Select(i => Path.GetFullPath(primaryOutputs[i].Path, outputBasePath));
                }
            }

            // Otherwise, return all project files from primary outputs (official SDK behavior)
            return primaryOutputs.Select(output => Path.GetFullPath(output.Path, outputBasePath));
        }
        
        /// <summary>
        /// Finds the first solution file in the directory tree starting from outputBasePath.
        /// </summary>
        private static string? FindSolutionFile(string startPath)
        {
            var currentDir = new DirectoryInfo(startPath);
            
            while (currentDir != null)
            {
                var slnFile = currentDir.GetFiles("*.sln").FirstOrDefault() ?? 
                             currentDir.GetFiles("*.slnx").FirstOrDefault();
                
                if (slnFile != null)
                {
                    return slnFile.FullName;
                }
                
                currentDir = currentDir.Parent;
            }
            
            return null;
        }

        /// <summary>
        /// Adds project files to the solution using dotnet CLI.
        /// </summary>
        private static bool AddProjectsToSolution(string slnFile, IEnumerable<string> projectFiles, string workingDirectory)
        {
            var projectFilesList = projectFiles.ToList();
            if (!projectFilesList.Any())
            {
                Console.Error.WriteLine("❌ No project files to add.");
                return false;
            }

            var projectArgs = string.Join(" ", projectFilesList.Select(f => $"\"{f}\""));
            var command = $"sln \"{slnFile}\" add {projectArgs}";
            
            Console.WriteLine($"Running: dotnet {command}");

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = command,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"✅ Successfully added {projectFilesList.Count} project(s) to solution.");
                return true;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Console.Error.WriteLine($"❌ dotnet sln add failed (exit code {process.ExitCode})");
            if (!string.IsNullOrEmpty(stdout)) Console.Error.WriteLine($"stdout: {stdout}");
            if (!string.IsNullOrEmpty(stderr)) Console.Error.WriteLine($"stderr: {stderr}");
            
            return false;
        }
    }
}
