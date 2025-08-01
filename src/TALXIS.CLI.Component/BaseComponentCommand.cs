using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

/// <summary>
/// Base class for component commands that provides common functionality for create, delete, list, and explain operations.
/// </summary>
public abstract class BaseComponentCommand
{
    /// <summary>
    /// Gets the name of the component type (e.g., "app", "entity", "form").
    /// </summary>
    protected abstract string ComponentType { get; }

    /// <summary>
    /// Gets a description of what this component represents.
    /// </summary>
    protected abstract string ComponentDescription { get; }

    /// <summary>
    /// Creates a new component instance.
    /// </summary>
    /// <param name="name">The name of the component to create.</param>
    /// <param name="options">Additional options for component creation.</param>
    public virtual async Task CreateAsync(string name, ComponentOptions? options = null)
    {
        Console.WriteLine($"Creating {ComponentType}: {name}");
        
        if (options?.Verbose == true)
        {
            Console.WriteLine($"Component type: {ComponentType}");
            Console.WriteLine($"Description: {ComponentDescription}");
        }

        await PerformCreateAsync(name, options);
        
        Console.WriteLine($"✅ Successfully created {ComponentType}: {name}");
    }

    /// <summary>
    /// Deletes an existing component instance.
    /// </summary>
    /// <param name="name">The name of the component to delete.</param>
    /// <param name="options">Additional options for component deletion.</param>
    public virtual async Task DeleteAsync(string name, ComponentOptions? options = null)
    {
        Console.WriteLine($"Deleting {ComponentType}: {name}");
        
        if (options?.Force != true)
        {
            Console.Write($"Are you sure you want to delete {ComponentType} '{name}'? (y/N): ");
            var confirmation = Console.ReadLine();
            if (!string.Equals(confirmation?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }
        }

        await PerformDeleteAsync(name, options);
        
        Console.WriteLine($"✅ Successfully deleted {ComponentType}: {name}");
    }

    /// <summary>
    /// Lists all existing component instances.
    /// </summary>
    /// <param name="options">Additional options for listing components.</param>
    public virtual async Task ListAsync(ComponentOptions? options = null)
    {
        Console.WriteLine($"Listing all {ComponentType} components:");
        
        var components = await GetComponentListAsync(options);
        
        if (!components.Any())
        {
            Console.WriteLine($"No {ComponentType} components found.");
            return;
        }

        foreach (var component in components)
        {
            if (options?.Verbose == true)
            {
                Console.WriteLine($"  📦 {component.Name} - {component.Description}");
                if (!string.IsNullOrEmpty(component.Path))
                {
                    Console.WriteLine($"     📁 Path: {component.Path}");
                }
                if (component.LastModified.HasValue)
                {
                    Console.WriteLine($"     📅 Modified: {component.LastModified:yyyy-MM-dd HH:mm:ss}");
                }
            }
            else
            {
                Console.WriteLine($"  📦 {component.Name}");
            }
        }
        
        Console.WriteLine($"\nTotal: {components.Count()} {ComponentType} component(s)");
    }

    /// <summary>
    /// Explains what this component type is and how to use it.
    /// </summary>
    /// <param name="options">Additional options for explanation.</param>
    public virtual Task ExplainAsync(ComponentOptions? options = null)
    {
        Console.WriteLine($"📋 {ComponentType.ToUpperInvariant()} Component");
        Console.WriteLine($"Description: {ComponentDescription}");
        Console.WriteLine();
        Console.WriteLine("Available operations:");
        Console.WriteLine($"  • create  - Create a new {ComponentType} component");
        Console.WriteLine($"  • delete  - Delete an existing {ComponentType} component");
        Console.WriteLine($"  • list    - List all {ComponentType} components");
        Console.WriteLine($"  • explain - Show this explanation");
        Console.WriteLine();
        
        ProvideAdditionalExplanation();
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the actual component creation logic. Override in derived classes.
    /// </summary>
    /// <param name="name">The name of the component to create.</param>
    /// <param name="options">Additional options for component creation.</param>
    protected abstract Task PerformCreateAsync(string name, ComponentOptions? options);

    /// <summary>
    /// Performs the actual component deletion logic. Override in derived classes.
    /// </summary>
    /// <param name="name">The name of the component to delete.</param>
    /// <param name="options">Additional options for component deletion.</param>
    protected abstract Task PerformDeleteAsync(string name, ComponentOptions? options);

    /// <summary>
    /// Gets the list of existing components. Override in derived classes.
    /// </summary>
    /// <param name="options">Additional options for listing components.</param>
    protected abstract Task<IEnumerable<ComponentInfo>> GetComponentListAsync(ComponentOptions? options);

    /// <summary>
    /// Provides additional component-specific explanation. Override in derived classes if needed.
    /// </summary>
    protected virtual void ProvideAdditionalExplanation()
    {
        // Default implementation - can be overridden
    }
}
