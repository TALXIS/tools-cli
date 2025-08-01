using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

[CliCommand(
    Name = "app",
    Description = "Manage application components in TALXIS solutions.",
    ShortFormAutoGenerate = CliNameAutoGenerate.None, // don't use aliases
    Children = new[] {
        typeof(AppCreateCommand),
        typeof(AppDeleteCommand),
        typeof(AppListCommand),
        typeof(AppExplainCommand)
    }
)]
public class AppComponentCommand : BaseComponentCommand
{
    protected override string ComponentType => "app";
    protected override string ComponentDescription => "Application components that define business applications within a TALXIS solution.";

    protected override async Task PerformCreateAsync(string name, ComponentOptions? options)
    {
        Console.WriteLine($"Creating app scaffolding for: {name}");
        await Task.Delay(100); // Simulate work
    }

    protected override async Task PerformDeleteAsync(string name, ComponentOptions? options)
    {
        Console.WriteLine($"Removing app files for: {name}");
        await Task.Delay(100); // Simulate work
    }

    protected override async Task<IEnumerable<ComponentInfo>> GetComponentListAsync(ComponentOptions? options)
    {
        await Task.Delay(100); // Simulate work
        return new[]
        {
            new ComponentInfo
            {
                Name = "SampleApp",
                Description = "Sample application component",
                Path = "./src/apps/SampleApp",
                LastModified = DateTime.Now.AddDays(-5)
            }
        };
    }

    protected override void ProvideAdditionalExplanation()
    {
        Console.WriteLine("Apps in TALXIS represent business applications that:");
        Console.WriteLine("  • Group related entities, forms, and views");
        Console.WriteLine("  • Define application-specific business logic");
        Console.WriteLine("  • Provide a cohesive user experience");
    }
}

[CliCommand(Name = "create", Description = "Create a new app component.")]
public class AppCreateCommand
{
    [CliArgument(Description = "Name of the app to create.")]
    public required string Name { get; set; }

    public async Task RunAsync()
    {
        var app = new AppComponentCommand();
        var options = new ComponentOptions {  };
        await app.CreateAsync(Name, options);
    }
}

[CliCommand(Name = "delete", Description = "Delete an existing app component.")]
public class AppDeleteCommand
{
    [CliArgument(Description = "Name of the app to delete.")]
    public required string Name { get; set; }

    [CliOption(Description = "Force deletion without confirmation.")]
    public bool Force { get; set; }

    public async Task RunAsync()
    {
        var app = new AppComponentCommand();
        var options = new ComponentOptions { Force = Force  };
        await app.DeleteAsync(Name, options);
    }
}

[CliCommand(Name = "list", Description = "List all app components.")]
public class AppListCommand
{
    public async Task RunAsync()
    {
        var app = new AppComponentCommand();
        var options = new ComponentOptions {  };
        await app.ListAsync(options);
    }
}

[CliCommand(Name = "explain", Description = "Explain what app components are and how to use them.")]
public class AppExplainCommand
{
    public async Task RunAsync()
    {
        var app = new AppComponentCommand();
        var options = new ComponentOptions {  };
        await app.ExplainAsync(options);
    }
}
