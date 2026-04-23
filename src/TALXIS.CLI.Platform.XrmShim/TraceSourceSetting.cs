using System.Diagnostics;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Holds trace source configuration for a named trace source,
/// including its trace level and registered listeners.
/// </summary>
public class TraceSourceSetting
{
    public string SourceName { get; set; }
    public SourceLevels TraceLevel { get; set; }
    public Dictionary<string, TraceListener> TraceListeners { get; set; }

    private TraceSourceSetting()
    {
        TraceListeners = new Dictionary<string, TraceListener>();
    }

    public TraceSourceSetting(string sourceName, SourceLevels sourceLevels)
    {
        TraceListeners = new Dictionary<string, TraceListener>();
        SourceName = sourceName;
        TraceLevel = sourceLevels;
    }
}
