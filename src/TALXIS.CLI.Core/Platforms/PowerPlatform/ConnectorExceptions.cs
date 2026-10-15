namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>No connector in the environment matches what the caller named.</summary>
public sealed class ConnectorNotFoundException : ArgumentException
{
    public ConnectorNotFoundException(string connector)
        : base($"Connector '{connector}' was not found in this environment.")
        => Connector = connector;

    public string Connector { get; }
}

/// <summary>What the caller named matches more than one connector.</summary>
public sealed class AmbiguousConnectorException : ArgumentException
{
    public AmbiguousConnectorException(string connector, IReadOnlyList<string> candidates)
        : base($"The display name '{connector}' matches {candidates.Count} connectors: " +
               string.Join(", ", candidates) + ". Use the connector's logical name or apiId.")
    {
        Connector = connector;
        Candidates = candidates;
    }

    public string Connector { get; }

    /// <summary>The apiIds of the matching connectors.</summary>
    public IReadOnlyList<string> Candidates { get; }
}
