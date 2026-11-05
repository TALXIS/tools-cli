namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// Option and argument wording for a connector identifier, shared so every
/// command that takes one says the same thing.
/// </summary>
internal static class ConnectorDescriptions
{
    /// <summary>What a connector identifier may be.</summary>
    public const string Identifier = "Connector logical name (for example almlab_connectorsopenfoodfacts or shared_office365users), apiId or exact display name.";

    /// <summary>The order the connector, connection and connection-reference commands are used in, and what separates the last two.</summary>
    public const string Workflow = "Typical order: connector list, connection create, connection permission add (share), connection-reference create/update (bind), then flows. A connection is the stored sign-in (credentials, a different id per environment, never exported); a connection reference is the solution-safe logical-name pointer that flows bind to.";

    /// <summary>A connector given only to disambiguate a connection id.</summary>
    public const string Disambiguator = "Connector of the connection (logical name, apiId or exact display name). Optional: only needed when the same connection id exists under several connectors.";
}
