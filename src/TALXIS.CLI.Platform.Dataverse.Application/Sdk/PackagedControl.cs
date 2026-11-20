namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// A PCF control shipped inside a solution zip, identified by its publisher-prefixed name.
/// </summary>
public sealed record PackagedControl(string Name, string Version);
