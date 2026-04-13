using System.Diagnostics;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Static trace settings matching the legacy Connector surface.
/// CrmPackageCore references <c>TraceControlSettings.TraceLevel</c>
/// and <c>TraceControlSettings.AddTraceListener</c>.
/// </summary>
public static class TraceControlSettings
{
    private static readonly List<TraceListener> _listeners = new();

    public static SourceLevels TraceLevel { get; set; } = SourceLevels.Information;

    public static void AddTraceListener(TraceListener listener)
    {
        _listeners.Add(listener);
    }

    public static void CloseListeners()
    {
        foreach (var l in _listeners)
        {
            l.Flush();
            l.Close();
        }
        _listeners.Clear();
    }
}
