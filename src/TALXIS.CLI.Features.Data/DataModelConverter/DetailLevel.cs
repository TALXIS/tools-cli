namespace TALXIS.CLI.Features.Data.DataModelConverter;

/// <summary>How much of a solution's metadata the conversion emits.</summary>
public enum DetailLevel
{
    /// <summary>Everything the inputs declare.</summary>
    Full = 0,

    /// <summary>
    /// Only what shows how the app was built: tables the app is built on, and the
    /// columns something belonging to those tables refers to. Not a schema export.
    /// </summary>
    Minimal = 1
}
