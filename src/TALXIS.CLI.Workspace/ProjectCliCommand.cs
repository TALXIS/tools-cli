using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Work with MSBuild projects in your workspace (solutions, plugins, libraries, controls...)",
    Alias = "p"
)]
public class ProjectCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(
            Description = "Provides a summary of MSBuild/.NET project types which can be created in the workspace, how componets should be segmented between them, their structure and references. Use this tool whenever the user asks about projects in the workspace.",
            Name = "explain")]
    public class ProjectExplainCliCommand
    {
        public void Run(CliContext context)
        {
            Console.WriteLine(
      @"Use of projects in this repository

Project Types Supported:
  • Dataverse Solution (.cdsproj or .csproj): Used for defining Dataverse components and metadata. Build artifact is solution ZIP file.
  • Dataverse Package (.csproj): Bundles multiple solutions and custom logic for deployment; references other projects to form a unit of deployment. Build artifact is package ZIP file which contains all solutions, import config, data and deployment automation/migration code.
  • Dataverse Plugin (.csproj): Contains custom business logic, event handlers, and automation for Dataverse. Build artifact is a plugin DLL file.
  • Power Apps Component Framework control (.csproj): Implements custom controls for Power Apps. Build artifact is a PCF control bundle JS.
  • Power Apps Script Library (.csproj): Provides reusable scripts for Power Platform solutions. Build artifact is a script bundle JS file.
  • Power Platform Connector (.csproj): Defines custom connectors for Power Platform integrations.
  • Code App (.csproj): Builds fully custom frontend SPA hosted in Power Apps.
  • Other .NET-based projects (.csproj, .xproj): Any additional supporting libraries or tools.

To find information how repository should be organized, consult 'workspace explain' command/tool.
  
Development and Build:
  • All projects are added to the single solution file (.sln) which is placed in the root directory
  • MSBuild (dotnet build) is used to build all project types
  • Solution projects can have ProjectReferences to plugin, script, PCF, and Code App project types. When the solution is built, it pulls outputs of the referenced projects and places them into the solution artifact (the build output of the solution project).
  • Multiple solutions can be added as ProjectReferences to the package project. When the package is built, it composes ImportConfig.xml with the list of solutions and the order in which they are imported to Dataverse upon deployment.
  • Package project type can contain C# migration and deployment automation/infra code. Dataverse packages can also contain configuration and test data which needs to be deployed with the code/definitions.
"
            );
        }
    }
}
