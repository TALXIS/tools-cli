namespace TALXIS.CLI.Platform.Dataverse.Application;

/// <summary>
/// UTC normalisation helpers. Dataverse returns UTC but the SDK often surfaces
/// values with <see cref="DateTimeKind.Unspecified"/>; callers must not re-apply
/// a local offset. Any user-supplied <see cref="DateTime"/> is converted to UTC.
/// </summary>
public static class DataverseDateTime
{
    /// <summary>
    /// Returns <paramref name="value"/> as UTC. Local values are converted; Unspecified
    /// is assumed to already be UTC (which matches what Dataverse returns).
    /// </summary>
    public static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static DateTime? EnsureUtc(DateTime? value) => value is null ? null : EnsureUtc(value.Value);
}
