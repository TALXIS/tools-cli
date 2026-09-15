using System.Text.Json;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Maps the Power Platform admin API <c>properties.environmentSku</c> string to
/// the strongly-typed <see cref="EnvironmentType"/>. Centralised so the catalog
/// (reading existing environments) and the provisioner (echoing the created
/// environment's type) agree on one mapping.
/// </summary>
internal static class EnvironmentSkuParser
{
    /// <summary>
    /// Reads <c>environmentSku</c> from a <c>properties</c> JSON object and maps
    /// it to <see cref="EnvironmentType"/>, or <c>null</c> when absent/unknown.
    /// </summary>
    public static EnvironmentType? TryParse(JsonElement properties)
    {
        if (!properties.TryGetProperty("environmentSku", out var skuElement)
            || skuElement.ValueKind != JsonValueKind.String)
            return null;

        return TryParse(skuElement.GetString());
    }

    /// <summary>Maps an <c>environmentSku</c> string to <see cref="EnvironmentType"/>.</summary>
    public static EnvironmentType? TryParse(string? sku)
        => sku?.Trim().ToLowerInvariant() switch
        {
            "production" => EnvironmentType.Production,
            "sandbox" => EnvironmentType.Sandbox,
            "trial" => EnvironmentType.Trial,
            "developer" => EnvironmentType.Developer,
            "default" => EnvironmentType.Default,
            "teams" => EnvironmentType.Teams,
            "subscriptionbasedtrial" => EnvironmentType.SubscriptionBasedTrial,
            _ => null,
        };
}
