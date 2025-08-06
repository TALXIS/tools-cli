using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Develop solutions in your local workspace",
    Alias = "ws",
    Children = new[]
    {
        typeof(ComponentCliCommand)
    })]
public class WorkspaceCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(
        Description = "Provides a summary of repository structure, build system, project types, components, and how they are organized for development and deployment. Use this tool whenever the user asks about organizing or understanding the workspace. This includes details about the monorepo layout, use of .NET project and solution files, supported project types (such as Dataverse solutions, plugins, packages, Power Apps components, and custom code apps), and how projects are managed and built using MSBuild. The summary also explains how solution and package projects reference and compose other components for deployment, offering flexibility in organizing code for various scenarios.",
        Name = "explain")]
    public class WorkspaceExplainCliCommand
    {
        public void Run(CliContext context)
        {
            Console.WriteLine(
@"Repository Structure Overview

This repository is organized as a monorepo based on the .NET project system. It uses a Visual Studio solution file (.sln) to track all projects, and each project is defined by its own project file (.csproj, .cdsproj, or other .(x)proj formats). All projects are built using MSBuild.

  • The root contains essential files such as README.md, .gitignore, and the .sln solution file.
  • The src/ directory holds all source code, organized into folders for different project types.

Project Types Supported:
  • Dataverse Solution (.cdsproj or .csproj): Used for defining Dataverse components and metadata.
  • Dataverse Package (.csproj): Bundles multiple solutions and custom logic for deployment; references other projects to form a unit of deployment.
  • Dataverse Plugin (.csproj): Contains custom business logic, event handlers, and automation for Dataverse.
  • Power Apps Component Framework control (.csproj): Implements custom controls for Power Apps.
  • Power Apps Script Library (.csproj): Provides reusable scripts for Power Platform solutions.
  • Power Platform Connector (.csproj): Defines custom connectors for Power Platform integrations.
  • Code App (.csproj): Builds fully custom frontend SPA hosted in Power Apps.
  • Other .NET-based projects (.csproj, .xproj): Any additional supporting libraries or tools.

Development and Build:
  • Projects are added to the solution file (.sln) for easy management and building.
  • MSBuild is used to build all project types, ensuring compatibility and automation.
  • Solution projects can have ProjectReferences to plugin, script, PCF, and Code App project types. When the solution is built, it pulls outputs of the referenced projects and places them into the solution artifact (the build output of the solution project).
  • Multiple solutions can be added as ProjectReferences to the package project. When the package is built, it composes ImportConfig.xml with the list of solutions and the order in which they are imported to Dataverse upon deployment.
  • Package project type can contain C# migration and deployment automation/infra code. Dataverse packages can also contain configuration and test data which needs to be deployed with the code/definitions.

This structure is flexible and does not enforce a specific layering or project type arrangement. Developers are free to organize solutions, plugins, packages, and other projects as needed for their scenario. The .NET project system and solution file provide a unified way to manage, build, and deploy all components in the repository."
            );
        }
    }
}
