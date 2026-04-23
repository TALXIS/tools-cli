using System.Diagnostics;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Global store for <see cref="TraceSourceSetting"/> instances,
/// used by CrmConnectControl and other Xrm Tooling components.
/// </summary>
public class TraceSourceSettingStore
{
    public static List<TraceSourceSetting> TraceSourceSettingsCollection { get; private set; } = new();

    public static void AddTraceSettingsToStore(TraceSourceSetting listnerSettings)
    {
        Trace.AutoFlush = true;
        if (listnerSettings == null) return;

        TraceSourceSetting? existing = TraceSourceSettingsCollection
            .SingleOrDefault(x => string.Compare(x.SourceName, listnerSettings.SourceName, StringComparison.OrdinalIgnoreCase) == 0);

        if (existing != null)
        {
            TraceSourceSettingsCollection.Remove(existing);
        }

        TraceSourceSettingsCollection.Add(listnerSettings);
    }

    public static TraceSourceSetting? GetTraceSourceSettings(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;

        return TraceSourceSettingsCollection
            .SingleOrDefault(x => string.Compare(x.SourceName, sourceName, StringComparison.OrdinalIgnoreCase) == 0);
    }
}
