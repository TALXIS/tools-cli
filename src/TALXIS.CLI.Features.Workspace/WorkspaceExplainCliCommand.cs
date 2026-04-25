using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Workspace;

[CliReadOnly]
[CliCommand(
    Description = "Provides a summary of repository structure, build system, project types, components, and how they are organized for development and deployment. Use this tool whenever the user asks about organizing or understanding the workspace. This includes details about the monorepo layout, use of .NET project and solution files, supported project types (such as Dataverse solutions, plugins, packages, Power Apps components, and custom code apps), and how projects are managed and built using MSBuild. The summary also explains how solution and package projects reference and compose other components for deployment, offering flexibility in organizing code for various scenarios.",
    Name = "explain")]
public class WorkspaceExplainCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(WorkspaceExplainCliCommand));

    private const string ExplanationText =
@"Repository Structure Overview

This repository is organized as a monorepo based on the .NET project system. It uses a Visual Studio solution file (.sln) to track all projects, and each project is defined by its own project file (.csproj, .cdsproj, or other .(x)proj formats). All projects are built using MSBuild.

Rules for repository root unless user explicitly instructs to do it differently:

  • The root contains essential files such as README.md, .gitignore, and the .sln solution file
  • The src/ directory holds all source code, organized into folders for different project types

You can find supported project types with 'workspace project explain' command/tool.

Repository Initialization Sequence:
  The Visual Studio solution file (.sln) and src folder MUST be initialized before creating any solutions or projects.**
  
  Required setup sequence:
  1. Initialize Git repository if it is not initialized already: git init -b 'main'
  2. Create .gitignore: dotnet new gitignore
  3. **Create Visual Studio solution file: dotnet new sln --name [ProjectName]**
  4. Create src/ directory: mkdir src
  
This structure is flexible and does not enforce a specific segmentation, layering or project arrangement. Developers are free to organize solutions, plugins, packages, and other projects as needed for their scenario. The .NET project system and Visual Studio solution file provide a unified way to manage, build, and deploy all components in the repository.";

    protected override Task<int> ExecuteAsync()
    {
        OutputFormatter.WriteValue("explanation", ExplanationText);
        return Task.FromResult(ExitSuccess);
    }
}
