namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// Option and argument wording for a connector identifier, shared so every
/// command that takes one says the same thing.
/// </summary>
internal static class ConnectorDescriptions
{
    /// <summary>What a connector identifier may be.</summary>
    public const string Identifier = "Connector logical name (for example almlab_connectorsopenfoodfacts or shared_office365users), apiId or exact display name.";

    /// <summary>A connector given only to disambiguate a connection id.</summary>
    public const string Disambiguator = "Connector of the connection (logical name, apiId or exact display name). Optional: only needed when the same connection id exists under several connectors.";
}
