using System.Reflection;
using System.Text;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Reflects over the TALXIS.TestKit.Bindings assembly to discover all Reqnroll step bindings
/// and builds a catalog of Gherkin patterns for use by the guide_testing endpoint.
/// </summary>
public class TestingBindingsCatalog
{
    private readonly List<StepBindingEntry> _entries = new();
    private string? _cachedCatalogPrompt;

    /// <summary>
    /// Loads step bindings from the TALXIS.TestKit.Bindings assembly using reflection.
    /// Scans for classes marked with [Binding] and extracts [Given], [When], [Then] patterns.
    /// </summary>
    public void Load()
    {
        var assembly = FindTestKitBindingsAssembly();
        if (assembly is null) return;

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!HasBindingAttribute(type))
                continue;

            var category = DeriveCategoryFromTypeName(type.Name);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                foreach (var attr in method.GetCustomAttributes(inherit: false))
                {
                    var (stepType, pattern) = ExtractStepPattern(attr);
                    if (stepType is null || pattern is null) continue;

                    var description = GetMethodSummary(method);

                    _entries.Add(new StepBindingEntry
                    {
                        StepType = stepType,
                        Pattern = pattern,
                        Category = category,
                        Description = description,
                        SourceClass = type.Name
                    });
                }
            }
        }

        _cachedCatalogPrompt = null;
    }

    /// <summary>
    /// Builds a formatted catalog string for inclusion in sampling prompts.
    /// Groups step bindings by category and step type.
    /// </summary>
    public string GetCatalogPrompt()
    {
        if (_cachedCatalogPrompt is not null)
            return _cachedCatalogPrompt;

        var sb = new StringBuilder();
        sb.AppendLine("# Available Reqnroll Step Bindings (TALXIS.TestKit.Bindings)");
        sb.AppendLine();
        sb.AppendLine("These are pre-built Gherkin step bindings for Power Apps UI test automation.");
        sb.AppendLine("Parameters in patterns are denoted by regex groups like '(.*)' — replace with actual values in quotes.");
        sb.AppendLine();

        var grouped = _entries
            .GroupBy(e => e.Category)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");

            foreach (var entry in group.OrderBy(e => e.StepType).ThenBy(e => e.Pattern))
            {
                var gherkinPattern = FormatAsGherkin(entry);
                sb.AppendLine($"- {gherkinPattern}");
                if (!string.IsNullOrWhiteSpace(entry.Description))
                    sb.AppendLine($"  *{entry.Description}*");
            }

            sb.AppendLine();
        }

        _cachedCatalogPrompt = sb.ToString();
        return _cachedCatalogPrompt;
    }

    /// <summary>
    /// Number of discovered step bindings.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets all discovered entries.
    /// </summary>
    public IReadOnlyList<StepBindingEntry> Entries => _entries;

    /// <summary>
    /// Formats a step binding entry as a Gherkin step line.
    /// Converts regex patterns to user-friendly placeholder syntax.
    /// </summary>
    private static string FormatAsGherkin(StepBindingEntry entry)
    {
        // Convert regex groups like '(.*)' to '{param}' for readability
        var readablePattern = entry.Pattern
            .Replace("'(.*)'", "'{value}'")
            .Replace("([^']+)", "{value}")
            .Replace("(.*)", "{value}")
            .Replace(@"(\d+)", "{number}")
            .Replace(@"(should|should not)", "{should|should not}");

        return $"{entry.StepType} {readablePattern}";
    }

    /// <summary>
    /// Extracts step type and pattern from a Reqnroll attribute instance.
    /// Supports Given, When, Then (and their aliases).
    /// </summary>
    private static (string? stepType, string? pattern) ExtractStepPattern(object attribute)
    {
        var attrType = attribute.GetType();
        var attrName = attrType.Name;

        string? stepType = attrName switch
        {
            "GivenAttribute" => "Given",
            "WhenAttribute" => "When",
            "ThenAttribute" => "Then",
            "StepDefinitionAttribute" => "Step",
            _ => null
        };

        if (stepType is null) return (null, null);

        // The pattern is stored in the Regex property (Reqnroll attribute base class)
        var regexProp = attrType.GetProperty("Regex");
        var pattern = regexProp?.GetValue(attribute) as string;

        return (stepType, pattern);
    }

    /// <summary>
    /// Derives a category name from the step binding class name.
    /// E.g., "NavigationSteps" -> "Navigation", "EntitySubGridSteps" -> "Entity Sub Grid"
    /// </summary>
    private static string DeriveCategoryFromTypeName(string typeName)
    {
        // Remove "Steps" suffix
        var name = typeName.EndsWith("Steps", StringComparison.Ordinal)
            ? typeName[..^5]
            : typeName;

        // Insert spaces before uppercase letters for readability
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Attempts to extract XML documentation summary from the method.
    /// Falls back to null if not available (XML docs are rarely embedded in NuGet packages).
    /// </summary>
    private static string? GetMethodSummary(MethodInfo method)
    {
        // XML documentation is typically not available via reflection at runtime.
        // We rely on the Gherkin pattern being self-documenting.
        return null;
    }

    /// <summary>
    /// Checks if a type has the Reqnroll [Binding] attribute.
    /// Uses name-based check to avoid version coupling.
    /// </summary>
    private static bool HasBindingAttribute(Type type)
    {
        return type.GetCustomAttributes(inherit: false)
            .Any(a => a.GetType().Name == "BindingAttribute");
    }

    /// <summary>
    /// Finds the TALXIS.TestKit.Bindings assembly.
    /// </summary>
    private static Assembly? FindTestKitBindingsAssembly()
    {
        // Try already loaded assemblies first
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TALXIS.TestKit.Bindings");

        if (loaded is not null) return loaded;

        // Try explicit load
        try
        {
            return Assembly.Load("TALXIS.TestKit.Bindings");
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Represents a single discovered step binding from TALXIS.TestKit.Bindings.
/// </summary>
public class StepBindingEntry
{
    /// <summary>
    /// The step type: Given, When, or Then.
    /// </summary>
    public required string StepType { get; init; }

    /// <summary>
    /// The regex pattern from the attribute (e.g., "I am logged in to the '(.*)' app as '(.*)'").
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Category derived from the source class (e.g., "Navigation", "Entity").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Optional description from XML documentation.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The source class name (e.g., "NavigationSteps").
    /// </summary>
    public required string SourceClass { get; init; }
}
