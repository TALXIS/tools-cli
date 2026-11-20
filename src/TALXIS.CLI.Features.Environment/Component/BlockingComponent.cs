namespace TALXIS.CLI.Features.Environment.Component;

/// <summary>
/// A component that still depends on the one being deleted, with its display name when it could be read.
/// </summary>
public sealed record BlockingComponent(string Type, Guid Id, string? Name);
