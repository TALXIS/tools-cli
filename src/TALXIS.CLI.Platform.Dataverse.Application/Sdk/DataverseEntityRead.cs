using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Small helpers for reading attribute values off an <see cref="Entity"/> in a
/// shape-tolerant way. Centralizes conversions that would otherwise be
/// copy-pasted into every reader's <c>ToRecord</c>.
/// </summary>
internal static class DataverseEntityRead
{
    /// <summary>
    /// Reads a <see cref="Guid"/>-valued attribute. Dataverse may surface a
    /// <c>uniqueidentifier</c> column as a <see cref="Guid"/> or as a string
    /// (e.g. on Web API / virtual entities); both are handled. Returns
    /// <c>null</c> when the attribute is absent or unparseable.
    /// </summary>
    public static Guid? ReadGuid(Entity entity, string attribute)
    {
        if (!entity.Contains(attribute)) return null;
        return entity[attribute] switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }
}
