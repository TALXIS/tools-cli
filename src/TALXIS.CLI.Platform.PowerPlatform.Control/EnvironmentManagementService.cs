using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Profile-resolving orchestrator for tenant-level environment administration.
/// Mirrors <see cref="EnvironmentSettingsService"/>: it owns the
/// (Profile, Connection, Credential) resolution and delegates the BAP admin
/// API work to the reusable catalog (list) and provisioner (create). The
/// connection's credential and cloud supply the admin authority — the target
/// environment URL is irrelevant for these tenant-scoped operations.
/// </summary>
public sealed class EnvironmentManagementService : IEnvironmentManagementService
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly IPowerPlatformEnvironmentProvisioner _provisioner;

    public EnvironmentManagementService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        IPowerPlatformEnvironmentProvisioner provisioner)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    }

    public async Task<IReadOnlyList<EnvironmentInfo>> ListAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environments = await _catalog.ListAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);

        return environments
            .Select(e => new EnvironmentInfo(
                e.EnvironmentId,
                e.DisplayName,
                e.EnvironmentUrl,
                e.UniqueName,
                e.OrganizationId,
                e.EnvironmentType))
            .ToList();
    }

    public async Task<EnvironmentCreateOutcome> CreateAsync(
        string? profileName,
        EnvironmentCreateOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);

        var request = new EnvironmentCreateRequest
        {
            DisplayName = options.DisplayName,
            EnvironmentType = options.EnvironmentType,
            Region = options.Region,
            CurrencyCode = options.CurrencyCode,
            Language = options.Language,
            DomainName = options.DomainName,
            Templates = options.Templates,
            SecurityGroupId = options.SecurityGroupId,
            UserObjectId = options.UserObjectId,
            Wait = options.Wait,
            MaxWait = options.MaxWait,
        };

        var result = await _provisioner.CreateAsync(ctx.Connection, ctx.Credential, request, ct).ConfigureAwait(false);

        return new EnvironmentCreateOutcome(
            result.EnvironmentId,
            result.DisplayName,
            result.EnvironmentUrl,
            result.EnvironmentType,
            result.Status,
            result.Completed,
            result.OperationLocation);
    }

    public async Task<EnvironmentUpdateOutcome> UpdateAsync(
        string? profileName,
        EnvironmentUpdateOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);

        var request = new EnvironmentUpdateRequest
        {
            EnvironmentId = options.EnvironmentId,
            DisplayName = options.DisplayName,
            EnvironmentType = options.EnvironmentType,
            SecurityGroupId = options.SecurityGroupId,
        };

        var result = await _provisioner.UpdateAsync(ctx.Connection, ctx.Credential, request, ct).ConfigureAwait(false);

        return new EnvironmentUpdateOutcome(
            result.EnvironmentId,
            result.DisplayName,
            result.EnvironmentType,
            result.Status);
    }

    public async Task<EnvironmentDeleteOutcome> DeleteAsync(
        string? profileName,
        Guid environmentId,
        bool wait,
        TimeSpan maxWait,
        CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var result = await _provisioner.DeleteAsync(ctx.Connection, ctx.Credential, environmentId, wait, maxWait, ct).ConfigureAwait(false);

        return new EnvironmentDeleteOutcome(
            result.EnvironmentId,
            result.Status,
            result.Completed,
            result.OperationLocation);
    }
}
