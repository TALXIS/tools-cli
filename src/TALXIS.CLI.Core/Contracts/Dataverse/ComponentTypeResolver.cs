namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Maps between integer component-type codes and human-friendly names.
/// Supports both directions: code → name and name → code.
/// Platform types are hardcoded; SCF types can be loaded dynamically from the environment.
/// </summary>
public sealed class ComponentTypeResolver
{
    private readonly Dictionary<int, string> _codeToName;
    private readonly Dictionary<string, int> _nameToCode;

    public ComponentTypeResolver()
    {
        _codeToName = new Dictionary<int, string>(PlatformTypes);
        _nameToCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in PlatformTypes)
            _nameToCode[name] = code;
        // Register common aliases
        foreach (var (alias, code) in PlatformAliases)
            _nameToCode[alias] = code;
    }

    /// <summary>Resolves a friendly name to its integer type code.</summary>
    public bool TryResolveCode(string nameOrCode, out int code)
    {
        if (int.TryParse(nameOrCode, out code))
            return _codeToName.ContainsKey(code) || code > 0;
        return _nameToCode.TryGetValue(nameOrCode, out code);
    }

    /// <summary>Returns all known friendly names for use in error messages.</summary>
    public IEnumerable<string> GetKnownNames() => _nameToCode.Keys.Order();

    /// <summary>Resolves an integer type code to a friendly name.</summary>
    public string ResolveName(int code)
        => _codeToName.TryGetValue(code, out var name) ? name : code.ToString();

    /// <summary>Well-known platform component types (static, same across all environments).</summary>
    private static readonly Dictionary<int, string> PlatformTypes = new()
    {
        [1] = "Entity",
        [2] = "Attribute",
        [3] = "Relationship",
        [9] = "OptionSet",
        [10] = "EntityRelationship",
        [14] = "EntityKey",
        [16] = "Privilege",
        [20] = "Role",
        [26] = "SavedQuery",
        [29] = "Workflow",
        [31] = "Report",
        [36] = "EmailTemplate",
        [59] = "SavedQueryVisualization",
        [60] = "SystemForm",
        [61] = "WebResource",
        [62] = "SiteMap",
        [63] = "ConnectionRole",
        [66] = "CustomControl",
        [70] = "FieldSecurityProfile",
        [80] = "AppModule",
        [91] = "PluginAssembly",
        [92] = "SdkMessageProcessingStep",
        [95] = "ServiceEndpoint",
        [300] = "CanvasApp",
        [371] = "Connector",
        [380] = "EnvironmentVariableDefinition",
        [381] = "EnvironmentVariableValue",
    };

    /// <summary>Common aliases developers might use on the command line.</summary>
    private static readonly Dictionary<string, int> PlatformAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Table"] = 1,
        ["Column"] = 2,
        ["Choice"] = 9,
        ["View"] = 26,
        ["Chart"] = 59,
        ["Form"] = 60,
        ["Dashboard"] = 60,
        ["SecurityRole"] = 20,
        ["Process"] = 29,
        ["PluginStep"] = 92,
        ["EnvironmentVariable"] = 380,
    };
}
