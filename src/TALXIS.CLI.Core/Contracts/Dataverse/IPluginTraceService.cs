namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Organization-wide plugin trace log level (the <c>plugintracelogsetting</c>
/// attribute on the <c>organization</c> entity). Controls whether plugin
/// execution is written to the <c>plugintracelog</c> table.
/// </summary>
public enum PluginTraceLevel
{
    Off = 0,
    Exception = 1,
    All = 2,
}

public sealed record PluginTraceSetting(
    Guid OrganizationId,
    string? OrganizationName,
    PluginTraceLevel Level);

public interface IPluginTraceService
{
    Task<PluginTraceSetting> GetSettingAsync(string? profileName, CancellationToken ct);

    Task<PluginTraceSetting> SetSettingAsync(string? profileName, PluginTraceLevel level, CancellationToken ct);
}
