namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record PublisherRecord(
    Guid Id,
    string UniqueName,
    string? FriendlyName,
    string? CustomizationPrefix,
    int OptionValuePrefix);

public sealed record PublisherCreateOptions(
    string UniqueName,
    string FriendlyName,
    string CustomizationPrefix,
    int OptionValuePrefix,
    string? Description);

public interface IPublisherService
{
    Task<IReadOnlyList<PublisherRecord>> ListAsync(string? profileName, CancellationToken ct);
    Task<PublisherRecord?> ShowAsync(string? profileName, string uniqueName, CancellationToken ct);
    Task<Guid> CreateAsync(string? profileName, PublisherCreateOptions options, CancellationToken ct);
}
