namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// A solution inside a deployable package, read from its solution.xml.
/// </summary>
public sealed record PackageSolutionInfo(string UniqueName, string? Version);
