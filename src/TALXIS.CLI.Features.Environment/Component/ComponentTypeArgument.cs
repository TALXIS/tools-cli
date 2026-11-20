using Microsoft.Extensions.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component;

/// <summary>
/// Resolves a component type argument given as a name, alias or integer code.
/// </summary>
internal static class ComponentTypeArgument
{
    /// <summary>
    /// Returns the type code and display name, or null (with the error logged) when the value is not a known name or a positive integer.
    /// </summary>
    public static (int Code, string Name)? TryResolve(string? value, ILogger logger)
    {
        var def = ComponentDefinitionRegistry.GetByName(value);
        if (def is not null) return ((int)def.TypeCode, def.Name);

        // Table-backed types (connection references, environment variables, ...) get environment-specific codes, so the server decides.
        if (int.TryParse(value, out var code) && code > 0)
        {
            def = ComponentDefinitionRegistry.GetByType((ComponentType)code);
            return (code, def?.Name ?? code.ToString());
        }

        var known = string.Join(", ", ComponentDefinitionRegistry.GetAll().Select(d => d.Name).Take(15));
        logger.LogError("Unknown component type '{Type}'. Available types: {Known}. Or use an integer code.", value, known);
        return null;
    }
}
