namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record SolutionCreateOptions(
    string UniqueName,
    string DisplayName,
    string PublisherUniqueName,
    string Version,
    string? Description);

public sealed record SolutionCreateOutcome(
    Guid SolutionId,
    string UniqueName,
    string Version);

public interface ISolutionCreateService
{
    Task<SolutionCreateOutcome> CreateAsync(
        string? profileName,
        SolutionCreateOptions options,
        CancellationToken ct);
}
