# Copilot Instructions for TALXIS CLI (`txc`)

## Project Overview

This repository contains the TALXIS CLI, a .NET global tool (alias: `txc`). The CLI is designed to help developers automate tasks and execute other useful operations over their local code repositories.

## Key Technologies

- **.NET (dotnet global tool)**
- **System.CommandLine** for parsing and handling CLI commands
- **Modular Command Groups**: Each command group is implemented as a separate class library for maintainability and scalability.

## Design Guidelines

- Organize commands into logical groups, each in its own class library.
- Use System.CommandLine’s features for argument parsing, help text, and subcommands.
- Ensure each command group is discoverable and testable independently.
- Follow .NET best practices for CLI tools (e.g., async/await, dependency injection where appropriate).

## Contribution Tips

- Add new command groups as separate class libraries.
- Register new commands in the main entry point.
- Write clear, concise help descriptions for each command and option.
- Include usage examples in the documentation and help output.

## Example Structure

```
/src
  /TALXIS.CLI           # Main entry point
  /TALXIS.CLI.Data      # Example command group
  ...
```

## Versioning

- The repository uses Microsoft-style versioning (e.g., 1.0.0.0) for all projects, set in `Directory.Build.props`.
- All projects share the same version number for unified packaging and release.
- Update the version in `Directory.Build.props` before publishing a new release.

## System.CommandLine 2.x Handler & Invocation Best Practices

- **Handler Registration:**
  - Use `SetAction` to bind logic to commands and subcommands. Example:
    ```csharp
    command.SetAction(parseResult => {
        var value = parseResult.GetValue(optionOrArgument);
        // ...do something...
        return 0;
    });
    ```
  - The action receives a `ParseResult` and should return an int exit code (or Task<int> for async).

- **Adding Arguments/Options/Subcommands:**
  - Use `.Arguments.Add()`, `.Options.Add()`, and `.Subcommands.Add()` to build up commands.
  - Example:
    ```csharp
    var arg = new Argument<string>("text") { Description = "Text to echo back." };
    var echo = new Command("echo", "Echoes input");
    echo.Arguments.Add(arg);
    parent.Subcommands.Add(echo);
    ```

- **Invocation:**
  - Parse and invoke with:
    ```csharp
    var parseResult = rootCommand.Parse(args);
    return parseResult.Invoke();
    ```
  - For async actions, use `InvokeAsync()`.

- **Getting Values:**
  - Use `parseResult.GetValue(optionOrArgument)` or `parseResult.GetValue<T>("name")`.

- **Error Handling:**
  - If you do not use actions, handle parse errors via `parseResult.Errors`.

- **Removed APIs:**
  - `Handler`, `SetHandler`, and `ICommandHandler` are removed in 2.x. Use `SetAction` exclusively.
  - `InvocationContext` is replaced by `ParseResult` in actions.

- **Migration:**
  - See [Migration Guide](https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5) for breaking changes and new patterns.

- **Install the package:**
  - `dotnet add package System.CommandLine --prerelease`

- **Command/Option/Argument Design:**
  - Use commands for actions or groups, options for parameters, and arguments for unnamed values.
  - Organize commands into logical groups and use subcommands for related actions.
  - Use kebab-case for names (e.g., `--my-option`).
  - Prefer verbs for commands (actions) and nouns for options.
  - Minimize short-form aliases; follow .NET conventions for common flags (e.g., `-o` for `--output`).
  - Make names lowercase and consistent in pluralization.

- **Parsing & Validation:**
  - Use `SetAction` to bind logic to commands.
  - Use validators to enforce constraints (e.g., positive numbers, file existence).
  - Use custom parsers for complex types or custom validation.
  - Handle parse errors and unmatched tokens for robust UX.

- **Tab Completion:**
  - Enable tab completion by installing the `dotnet-suggest` tool and adding the appropriate shim to your shell profile.
  - Register your CLI with `dotnet-suggest register --command-path $executableFilePath`.
  - Provide dynamic completions using `CompletionSources`.

- **Help & Usage:**
  - Write clear help descriptions for commands and options.
  - Use the built-in help and version options provided by System.CommandLine.

- **Testing & Extensibility:**
  - Test command groups independently.
  - Use modular class libraries for scalability.

- **Recommended Reading:**
  - [Get started tutorial](https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial)
  - [Syntax overview](https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax)
  - [Parsing and invocation](https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-parse-and-invoke)
  - [Custom parsing and validation](https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-customize-parsing-and-validation)
  - [Tab completion](https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-enable-tab-completion)
  - [Design guidance](https://learn.microsoft.com/en-us/dotnet/standard/commandline/design-guidance)
  - [API Reference](https://learn.microsoft.com/en-us/dotnet/api/system.commandline)

## .NET Global Tools: Usage & Best Practices

- **What is a .NET Global Tool?**
  - A .NET global tool is a special NuGet package containing a console app, installable for all directories and available on the user's PATH.
  - Tools can be installed globally, in a custom location (tool-path), or as local tools (per project/repo).

- **Installation:**
  - Install globally: `dotnet tool install -g <package-name>`
  - Install in custom location: `dotnet tool install <package-name> --tool-path <path>`
  - Install as a local tool (with manifest):
    1. `dotnet new tool-manifest` (if not present)
    2. `dotnet tool install <package-name>`
    3. Restore all local tools: `dotnet tool restore`

- **Usage:**
  - Invoke a global tool: `<tool-command>` (e.g., `txc`)
  - If the tool is prefixed with `dotnet-`, you can also use `dotnet <command>`
  - For local tools: `dotnet tool run <command>` or `dotnet <command>` from the directory with the manifest

- **Update/Uninstall:**
  - Update: `dotnet tool update --global <package-name>`
  - Uninstall: `dotnet tool uninstall --global <package-name>`

- **Best Practices:**
  - Only install tools from trusted sources (tools run in full trust).
  - Document the tool's usage and provide clear help output (`--help`).
  - For local tools, commit the manifest file to your repository for team consistency.
  - Use Microsoft-style versioning for consistency across all projects.

- **References:**
  - [How to manage .NET tools](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools)
  - [Create a .NET tool using the .NET CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create)
  - [Install and use a .NET global tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-use)
  - [Install and use a .NET local tool](https://learn.microsoft.com/en-us/dotnet/core/tools/local-tools-how-to-use)

## Creating and Publishing a Custom .NET Global Tool

To create and distribute a .NET global tool (latest stable .NET SDK):

1. **Prerequisites**
   - .NET SDK 9.0 (latest) or later
   - Any code/text editor

2. **Create the Project**
   ```sh
   dotnet new console -n <Your.Tool.Name> -f net9.0
   cd <Your.Tool.Name>
   ```

3. **Add Your Code**
   - Implement your CLI logic in `Program.cs` (use System.CommandLine for advanced parsing).

4. **Update the Project File for Tool Packaging**
   In your `.csproj` inside `<PropertyGroup>`, add:
   ```xml
   <PackAsTool>true</PackAsTool>
   <ToolCommandName>your-tool-command</ToolCommandName>
   <PackageOutputPath>./nupkg</PackageOutputPath>
   ```
   - `ToolCommandName` is the command users will run (e.g., `txc`).

5. **Build and Pack the Tool**
   ```sh
   dotnet pack
   ```
   - This creates a `.nupkg` file in the specified output path.

6. **Publish to NuGet**
   - Create an account on [nuget.org](https://www.nuget.org/).
   - Upload your `.nupkg` via the website or use the CLI:
     ```sh
     dotnet nuget push ./nupkg/<Your.Tool.Name>.<version>.nupkg --api-key <your-key> --source https://api.nuget.org/v3/index.json
     ```

7. **Install and Use the Tool**
   - Users can install globally:
     ```sh
     dotnet tool install -g <your-tool-name>
     ```
   - Or as a local tool (with a manifest):
     ```sh
     dotnet new tool-manifest
     dotnet tool install <your-tool-name>
     ```

**References:**
- [Create a .NET tool using the .NET CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create)
- [Publish a NuGet package](https://learn.microsoft.com/en-us/nuget/create-packages/publish-a-package)

