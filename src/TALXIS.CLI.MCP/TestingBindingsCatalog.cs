using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Reflects over the TALXIS.TestKit.Bindings assembly to discover all Reqnroll step bindings
/// and builds a catalog of Gherkin patterns for use by the guide_testing endpoint.
/// </summary>
public class TestingBindingsCatalog
{
    private readonly List<StepBindingEntry> _entries = new();
    private string? _cachedCatalogPrompt;
    private string? _resolvedVersion;

    /// <summary>
    /// Loads step bindings from the TALXIS.TestKit.Bindings assembly using reflection.
    /// Scans for classes marked with [Binding] and extracts [Given], [When], [Then] patterns.
    /// </summary>
    public void Load()
    {
        var assembly = FindTestKitBindingsAssembly();
        if (assembly is null) return;

        _resolvedVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        var xmlDocs = LoadXmlDocSummaries(assembly);

        // GetExportedTypes() throws (and gives up entirely) the moment ANY type in the assembly
        // fails to load — e.g. a transitive dependency (YamlDotNet, in practice) resolves to a
        // version incompatible with one the assembly was built against, for a type we don't even
        // care about here. GetTypes() + ReflectionTypeLoadException recovery still yields every
        // type that DID load, so one bad type can't take down the whole catalog — or, since this
        // runs at MCP server startup, the whole server.
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        catch (Exception)
        {
            // Nothing usable could be loaded from the assembly at all — leave the catalog empty
            // rather than let a third-party assembly's incompatibility crash the MCP server.
            return;
        }

        foreach (var type in types)
        {
            if (!type.IsPublic || !HasBindingAttribute(type))
                continue;

            var category = DeriveCategoryFromTypeName(type.Name);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                foreach (var attr in method.GetCustomAttributes(inherit: false))
                {
                    var (stepType, pattern) = ExtractStepPattern(attr);
                    if (stepType is null || pattern is null) continue;

                    var description = GetMethodSummary(method, xmlDocs);
                    var parameterNames = method.GetParameters().Select(p => p.Name ?? "value").ToList();

                    _entries.Add(new StepBindingEntry
                    {
                        StepType = stepType,
                        Pattern = pattern,
                        Category = category,
                        Description = description,
                        SourceClass = type.Name,
                        ParameterNames = parameterNames
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
        sb.AppendLine($"# Available Reqnroll Step Bindings (TALXIS.TestKit.Bindings v{_resolvedVersion})");
        sb.AppendLine();
        sb.AppendLine("This reflects the TestKit version bundled with the MCP server, not necessarily the");
        sb.AppendLine("version referenced by your workspace's test project — compare against your own");
        sb.AppendLine("PackageReference if a mapped step fails to bind at build time.");
        sb.AppendLine();
        sb.AppendLine("These are pre-built Gherkin step bindings for Power Apps UI test automation.");
        sb.AppendLine("Placeholders like '{value}' or '{fieldName}' mark where a step takes an argument —");
        sb.AppendLine("replace each with an actual value in quotes. A leading '*' means the step works as");
        sb.AppendLine("Given, When, or Then depending on context.");
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
    /// Walks the regex pattern's own parenthesis structure — rather than a fixed string-replace
    /// chain — so every top-level capturing group becomes a named placeholder (from the bound
    /// method's parameter name, in capture order, or the group's own name for `(?&lt;name&gt;...)`),
    /// while non-capturing groups, lookarounds, and anchors are left untouched instead of leaking
    /// raw regex into the output.
    /// </summary>
    private static string FormatAsGherkin(StepBindingEntry entry)
    {
        var pattern = entry.Pattern;
        var paramNames = entry.ParameterNames;
        var sb = new StringBuilder();
        int paramIndex = 0;
        int i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length)
            {
                sb.Append(c).Append(pattern[i + 1]);
                i += 2;
                continue;
            }

            if (c != '(')
            {
                sb.Append(c);
                i++;
                continue;
            }

            // '(?...)' is non-capturing, a lookaround, or a named group; a bare '(' starts a
            // capturing group. Named groups '(?<name>...)' still count as capturing.
            var isSpecial = i + 1 < pattern.Length && pattern[i + 1] == '?';
            string? namedGroupName = null;
            if (isSpecial && i + 2 < pattern.Length && pattern[i + 2] == '<'
                && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
            {
                var nameEnd = pattern.IndexOf('>', i + 3);
                if (nameEnd > i + 3)
                    namedGroupName = pattern[(i + 3)..nameEnd];
            }
            var isCapturing = !isSpecial || namedGroupName is not null;

            // Find the matching close paren, tracking nesting depth and skipping escapes.
            var depth = 1;
            var j = i + 1;
            while (j < pattern.Length && depth > 0)
            {
                if (pattern[j] == '\\' && j + 1 < pattern.Length) { j += 2; continue; }
                if (pattern[j] == '(') depth++;
                else if (pattern[j] == ')') depth--;
                j++;
            }

            if (isCapturing)
            {
                var name = namedGroupName ?? (paramIndex < paramNames.Count ? paramNames[paramIndex] : "value");
                paramIndex++;
                sb.Append('{').Append(name).Append('}');
            }
            else
            {
                // Non-capturing group, lookaround, etc. — keep the original regex text as-is.
                sb.Append(pattern, i, j - i);
            }

            i = j;
        }

        return $"{entry.StepType} {sb}";
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
            // [StepDefinition] matches Given, When, *and* Then — "Step" isn't a Gherkin keyword,
            // so a copied line would fail to parse. '*' is Gherkin's own "any step keyword" marker.
            "StepDefinitionAttribute" => "*",
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
    /// Looks up the method's XML documentation summary, if the assembly's doc file was found
    /// and loaded by <see cref="LoadXmlDocSummaries"/>.
    /// </summary>
    private static string? GetMethodSummary(MethodInfo method, IReadOnlyDictionary<string, string> xmlDocs)
    {
        return xmlDocs.TryGetValue(BuildXmlDocMemberId(method), out var summary) ? summary : null;
    }

    /// <summary>
    /// Loads &lt;summary&gt; text for every member from the assembly's sibling .xml doc file
    /// (e.g. TALXIS.TestKit.Bindings.xml next to TALXIS.TestKit.Bindings.dll), keyed by XML doc
    /// member ID. Best-effort: returns an empty map if the file isn't present or fails to parse.
    /// </summary>
    private static Dictionary<string, string> LoadXmlDocSummaries(Assembly assembly)
    {
        var result = new Dictionary<string, string>();
        try
        {
            if (string.IsNullOrEmpty(assembly.Location))
                return result;

            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
            if (!File.Exists(xmlPath))
                return result;

            var doc = XDocument.Load(xmlPath);
            foreach (var member in doc.Descendants("member"))
            {
                var name = (string?)member.Attribute("name");
                var summary = member.Element("summary")?.Value;
                if (name is null || summary is null)
                    continue;

                result[name] = string.Join(' ', summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch
        {
            // XML docs are a best-effort enhancement — fall back to no descriptions.
        }

        return result;
    }

    /// <summary>
    /// Builds the XML doc member ID for a method (e.g. "M:Namespace.Type.Method(System.String)"),
    /// matching the format used as the "name" attribute in .NET-generated XML documentation.
    /// </summary>
    private static string BuildXmlDocMemberId(MethodInfo method)
    {
        var sb = new StringBuilder("M:").Append(method.DeclaringType!.FullName).Append('.').Append(method.Name);

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            sb.Append('(');
            sb.Append(string.Join(",", parameters.Select(p => GetXmlDocTypeName(p.ParameterType))));
            sb.Append(')');
        }

        return sb.ToString();
    }

    private static string GetXmlDocTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var baseName = definition.FullName![..definition.FullName!.IndexOf('`')];
            var args = string.Join(",", type.GetGenericArguments().Select(GetXmlDocTypeName));
            return $"{baseName}{{{args}}}";
        }

        return type.FullName ?? type.Name;
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

    /// <summary>
    /// Names of the bound method's parameters, in declaration order. Used to name each regex
    /// capturing group's placeholder in <c>FormatAsGherkin</c> instead of a generic "{value}".
    /// </summary>
    public required IReadOnlyList<string> ParameterNames { get; init; }
}
