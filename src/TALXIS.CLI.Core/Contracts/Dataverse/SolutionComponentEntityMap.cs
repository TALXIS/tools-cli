namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Maps record-backed solution component types to the Dataverse entity that holds them,
/// so a component can be deleted from the environment with a plain record delete.
/// </summary>
public static class SolutionComponentEntityMap
{
    // Standard Dataverse solution component type codes -> owning entity logical name.
    private static readonly IReadOnlyDictionary<int, string> Map = new Dictionary<int, string>
    {
        [20] = "role",                      
        [26] = "savedquery",                
        [29] = "workflow",                  
        [60] = "systemform",                
        [61] = "webresource",               
        [70] = "fieldsecurityprofile",      
        [80] = "appmodule",                 
        [91] = "pluginassembly",            
        [92] = "sdkmessageprocessingstep",  
        [380] = "environmentvariabledefinition", 
    };

    /// <summary>
    /// Returns the owning entity logical name for a record-backed component type.
    /// </summary>
    public static bool TryGetEntityLogicalName(int componentType, out string? entityLogicalName) => Map.TryGetValue(componentType, out entityLogicalName);
        
    /// <summary>
    /// Whether this command can delete the given component type from the environment.
    /// </summary>
    public static bool IsSupported(int componentType) => Map.ContainsKey(componentType);

    /// <summary>
    /// Human-readable list of supported entities, for error messages.
    /// </summary>
    public static string SupportedSummary => string.Join(", ", Map.Values.OrderBy(v => v, StringComparer.Ordinal));
}
