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
    /// Returns null if parameters are invalid (logs the error).
    /// </summary>
    public static async Task<(string ComponentId, string TypeName)?> TryResolveAsync(
        string? id, string? type,
        string? entity, string? attribute,
        string? profile,
        ILogger logger,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entity))
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                logger.LogError("Use either --id or --entity, not both.");
                return null;
            }

            var resolver = TxcServices.Get<IMetadataIdResolver>();
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                var resolved = await resolver.ResolveAttributeIdAsync(profile, entity, attribute, ct).ConfigureAwait(false);
                return (resolved.ToString(), "Attribute");
            }
            else
            {
                var resolved = await resolver.ResolveEntityIdAsync(profile, entity, ct).ConfigureAwait(false);
                return (resolved.ToString(), "Entity");
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            logger.LogError("Provide --id <guid> --type <type>, or --entity <name> [--attribute <name>].");
            return null;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            logger.LogError("--type is required when using --id.");
            return null;
        }

        return (id, type);
    }
}
