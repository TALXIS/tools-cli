using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;

namespace TALXIS.CLI.Features.Environment.Component;

/// <summary>
/// Shared helper for resolving component identification from either
/// --id/--type or --entity/--attribute command parameters.
/// </summary>
internal static class ComponentIdResolver
{
    /// <summary>
    /// Resolves component ID and type name from either direct ID or entity/attribute names.
    /// Returns false if parameters are invalid (logs the error).
    /// </summary>
    public static bool TryResolve(
        string? id, string? type,
        string? entity, string? attribute,
        string? profile,
        ILogger logger,
        out string componentId, out string typeName)
    {
        componentId = "";
        typeName = "";

        if (!string.IsNullOrWhiteSpace(entity))
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                logger.LogError("Use either --id or --entity, not both.");
                return false;
            }

            var resolver = TxcServices.Get<IMetadataIdResolver>();
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                componentId = resolver.ResolveAttributeIdAsync(profile, entity, attribute, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult().ToString();
                typeName = "Attribute";
            }
            else
            {
                componentId = resolver.ResolveEntityIdAsync(profile, entity, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult().ToString();
                typeName = "Entity";
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            logger.LogError("Provide --id <guid> --type <type>, or --entity <name> [--attribute <name>].");
            return false;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            logger.LogError("--type is required when using --id.");
            return false;
        }

        componentId = id;
        typeName = type;
        return true;
    }
}
