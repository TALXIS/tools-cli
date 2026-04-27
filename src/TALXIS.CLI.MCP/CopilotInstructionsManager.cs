using System;
using System.IO;
using System.Threading.Tasks;

namespace TALXIS.CLI.MCP
{
    /// <summary>
    /// Internal utility for managing Copilot instructions in user projects.
    /// Not exposed as a CLI command.
    /// </summary>
    public class CopilotInstructionsManager
    {
        // Markdown markers for TALXIS CLI instructions section
        private const string StartMarker = "<!-- TALXIS CLI Instructions Start -->";
        private const string EndMarker = "<!-- TALXIS CLI Instructions End -->";
        
        // Hardcoded instructions to be written within the marked section
        private const string TalxisInstructions = @"
# Instructions for performing tasks over the repository
Use the TALXIS CLI MCP Server (txc-mcp) for all development tasks. Call `guide_workspace`, `guide_environment`, `guide_deployment`, `guide_data`, `guide_config`, or `get_skill_details` to discover available tools and guidance.

## Project Structure and Naming Conventions

**Note**: These are recommended naming conventions. Users may choose different naming styles based on their preferences or organizational standards.

### Repository Structure
```
├── src/                          # Source code directory
│   ├── Solutions.DataModel/      # Dataverse schema and data model
│   ├── Solutions.Logic/          # Business logic and plugins  
│   ├── Solutions.UI/            # User interface components
│   ├── Solutions.Security/      # Security roles and permissions
│   ├── Plugins.{Domain}/        # Plugin projects (e.g., Plugins.Warehouse)
│   └── Packages.Main/           # Package Deployer project
├── pipelines/                   # CI/CD pipeline definitions
│   ├── build.yml               # Build pipeline
│   ├── deploy.yml              # Deployment pipeline
│   └── test.yml                # Test pipeline
└── tests/                      # Test projects
```

### Solution Naming Patterns
- **Data Model**: `Solutions.DataModel` - Contains tables, columns, relationships
- **Business Logic**: `Solutions.Logic` - Contains plugins, workflows, business rules
- **User Interface**: `Solutions.UI` - Contains forms, views, model-driven apps
- **Security**: `Solutions.Security` - Contains security roles and privileges
- **Package**: `Packages.Main` - Main deployment package

### Plugin Project Naming
- Pattern: `Plugins.{DomainArea}`
- Examples: `Plugins.Warehouse`, `Plugins.Inventory`, `Plugins.Sales`
- Plugin classes: `{Action}{Entity}Plugin.cs` (e.g., `ValidateWarehouseTransactionPlugin.cs`)

### Entity Naming Examples
- Logical names: lowercase with publisher prefix (e.g., `publisherprefix_warehouseitem`)
- Display names: Proper case (e.g., `Warehouse Item`, `Warehouse Transaction`)
- Schema names: Include publisher prefix (e.g., `publisherprefix_warehouseitem`)

### Publisher Prefix Requirements
- **Required for most txc-mcp commands** when creating Dataverse components
- **Maximum 8 characters** - enforced by Dataverse platform
- Should be unique to your organization (e.g., `contoso`, `myorg`)
- Used as prefix for tables, columns, and other Dataverse components
- Example: With prefix `udpp`, table becomes `udpp_warehouseitem`

### Branch Naming
- Feature branches: `{userPrefix}/{feature-description}` (e.g., `user/add-data-model`)
- Main integration branch: `main`
- Use trunk-based development with short-lived feature branches";

        // Complete content with markers for new files
        private static readonly string DefaultFileContent = 
            $@"<!-- This file contains Copilot instructions. You can add your own instructions above or below the TALXIS CLI section. -->

{StartMarker}
{TalxisInstructions}
{EndMarker}
";

        /// <summary>
        /// Ensures that the .github/copilot-instructions.md file in the target directory contains the TALXIS CLI instructions
        /// within the marked section. Creates or updates the file as needed while preserving user content.
        /// </summary>
        /// <param name="targetDirectory">The root directory of the user project.</param>
        /// <returns>A result indicating the action taken.</returns>
        public async Task<CopilotInstructionsResult> EnsureCopilotInstructionsAsync(string targetDirectory)
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("Target directory must be provided.", nameof(targetDirectory));

            var githubDir = Path.Combine(targetDirectory, ".github");
            var instructionsPath = Path.Combine(githubDir, "copilot-instructions.md");

            // Ensure .github directory exists
            if (!Directory.Exists(githubDir))
                Directory.CreateDirectory(githubDir);

            // Check if file exists
            if (File.Exists(instructionsPath))
            {
                var existing = await File.ReadAllTextAsync(instructionsPath);
                var updated = UpdateTalxisSection(existing);
                
                if (updated == existing)
                    return CopilotInstructionsResult.UpToDate;

                await File.WriteAllTextAsync(instructionsPath, updated);
                return CopilotInstructionsResult.Updated;
            }
            else
            {
                await File.WriteAllTextAsync(instructionsPath, DefaultFileContent);
                return CopilotInstructionsResult.Created;
            }
        }

        /// <summary>
        /// Updates the TALXIS CLI instructions section in the existing content while preserving user content.
        /// If no marked section exists, adds it at the end.
        /// </summary>
        /// <param name="content">The existing file content.</param>
        /// <returns>The updated content with TALXIS CLI instructions.</returns>
        private string UpdateTalxisSection(string content)
        {
            var startIndex = content.IndexOf(StartMarker);
            var endIndex = content.IndexOf(EndMarker);

            // If both markers exist, replace the content between them
            if (startIndex >= 0 && endIndex >= 0 && endIndex > startIndex)
            {
                var beforeSection = content.Substring(0, startIndex);
                var afterSection = content.Substring(endIndex + EndMarker.Length);
                return $"{beforeSection}{StartMarker}\n{TalxisInstructions}\n{EndMarker}{afterSection}";
            }

            // If markers don't exist, add the complete marked section at the end
            if (!content.Contains(StartMarker) && !content.Contains(EndMarker))
            {
                var separator = content.EndsWith("\n") ? "" : "\n\n";
                return $"{content}{separator}{StartMarker}\n{TalxisInstructions}\n{EndMarker}\n";
            }

            // If only one marker exists (corrupted state), replace the whole content with default
            return DefaultFileContent;
        }
    }

    /// <summary>
    /// Result of the copilot instructions update operation.
    /// </summary>
    public enum CopilotInstructionsResult
    {
        Created = 0,
        Updated = 1,
        UpToDate = 2
    }
}
