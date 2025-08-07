using DotMake.CommandLine;
using System.ComponentModel;

namespace TALXIS.CLI.MCP
{
    /// <summary>
    /// CLI command for managing Copilot instructions in user projects.
    /// This command creates or updates .github/copilot-instructions.md with TALXIS CLI specific instructions.
    /// </summary>
    [CliCommand(
        Name = "copilot-instructions",
        Description = "Creates or updates .github/copilot-instructions.md file with TALXIS CLI instructions in the target project"
    )]
    public class CopilotInstructionsCliCommand
    {
        private readonly CopilotInstructionsManager _manager = new();

        /// <summary>
        /// The target directory where the copilot instructions should be created or updated.
        /// Defaults to current directory if not specified.
        /// </summary>
        [CliOption(
            Description = "Target directory where .github/copilot-instructions.md should be created or updated",
            Required = false
        )]
        [DefaultValue(".")]
        public string TargetDirectory { get; set; } = ".";

        /// <summary>
        /// Executes the copilot instructions update operation.
        /// </summary>
        /// <param name="context">The CLI context.</param>
        public async Task<int> RunAsync(CliContext context)
        {
            try
            {
                var result = await _manager.EnsureCopilotInstructionsAsync(TargetDirectory);
                
                switch (result)
                {
                    case CopilotInstructionsResult.Created:
                        Console.WriteLine($"✅ Created .github/copilot-instructions.md with TALXIS CLI instructions in {TargetDirectory}");
                        break;
                    case CopilotInstructionsResult.Updated:
                        Console.WriteLine($"✅ Updated TALXIS CLI instructions in .github/copilot-instructions.md in {TargetDirectory}");
                        break;
                    case CopilotInstructionsResult.UpToDate:
                        Console.WriteLine($"✅ TALXIS CLI instructions are already up-to-date in {TargetDirectory}");
                        break;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                return 1;
            }
        }
    }
}
