using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="IDataverseOptionSetService"/>.
/// Manages global option sets and individual option values using the
/// metadata API (<c>CreateOptionSetRequest</c>, <c>InsertOptionValueRequest</c>,
/// <c>DeleteOptionValueRequest</c>, <c>RetrieveAllOptionSetsRequest</c>).
/// </summary>
internal sealed class DataverseOptionSetService : IDataverseOptionSetService
{
    /// <inheritdoc />
    public async Task CreateGlobalOptionSetAsync(
        string? profileName,
        string schemaName,
        string displayName,
        string? description,
        OptionMetadataInput[] options,
        string? solution,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var optionSetMetadata = new OptionSetMetadata
        {
            Name = schemaName,
            DisplayName = new Label(displayName, 1033),
            IsGlobal = true,
            OptionSetType = OptionSetType.Picklist
        };

        if (description is not null)
            optionSetMetadata.Description = new Label(description, 1033);

        foreach (var opt in options)
            optionSetMetadata.Options.Add(new OptionMetadata(new Label(opt.Label, 1033), opt.Value));

        var request = new CreateOptionSetRequest { OptionSet = optionSetMetadata };

        if (!string.IsNullOrEmpty(solution))
            request["SolutionUniqueName"] = solution;

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InsertOptionAsync(
        string? profileName,
        string? entityName,
        string? attributeName,
        string? optionSetName,
        string label,
        int? value,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new InsertOptionValueRequest
        {
            Label = new Label(label, 1033)
        };

        if (value.HasValue)
            request.Value = value.Value;

        if (!string.IsNullOrWhiteSpace(optionSetName))
        {
            request.OptionSetName = optionSetName;
        }
        else
        {
            // Local option set — set entity and attribute.
            request.EntityLogicalName = entityName;
            request.AttributeLogicalName = attributeName;
        }

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOptionAsync(
        string? profileName,
        string? entityName,
        string? attributeName,
        string? optionSetName,
        int value,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new DeleteOptionValueRequest
        {
            Value = value
        };

        if (!string.IsNullOrWhiteSpace(optionSetName))
        {
            request.OptionSetName = optionSetName;
        }
        else
        {
            request.EntityLogicalName = entityName;
            request.AttributeLogicalName = attributeName;
        }

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteGlobalOptionSetAsync(
        string? profileName,
        string optionSetName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new DeleteOptionSetRequest { Name = optionSetName };
        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GlobalOptionSetDetailRecord> DescribeGlobalOptionSetAsync(
        string? profileName,
        string optionSetName,
        int? languageCode,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        conn.Client.ForceServerMetadataCacheConsistency = true;

        var request = new RetrieveOptionSetRequest { Name = optionSetName };
        var response = (RetrieveOptionSetResponse)
            await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        var os = response.OptionSetMetadata;
        var options = os is OptionSetMetadata osm
            ? osm.Options
                .Where(o => o.Value.HasValue)
                .OrderBy(o => o.Value!.Value)
                .Select(o => new OptionValueRecord(
                    Value: o.Value!.Value,
                    Label: LabelHelper.GetLabel(o.Label, languageCode),
                    Description: LabelHelper.GetLabel(o.Description, languageCode)))
                .ToList()
            : new List<OptionValueRecord>();

        return new GlobalOptionSetDetailRecord(
            Name: os.Name,
            DisplayName: LabelHelper.GetLabel(os.DisplayName, languageCode),
            Description: LabelHelper.GetLabel(os.Description, languageCode),
            OptionSetType: os.OptionSetType?.ToString() ?? "Unknown",
            IsCustomOptionSet: os.IsCustomOptionSet == true,
            Options: options);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GlobalOptionSetSummaryRecord>> ListGlobalOptionSetsAsync(
        string? profileName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Ensure fresh metadata after mutations (e.g. create/delete).
        conn.Client.ForceServerMetadataCacheConsistency = true;

        var response = (RetrieveAllOptionSetsResponse)
            await conn.Client.ExecuteAsync(new RetrieveAllOptionSetsRequest(), ct).ConfigureAwait(false);

        return response.OptionSetMetadata
            .Where(os => os.IsGlobal == true)
            .OrderBy(os => os.Name, StringComparer.OrdinalIgnoreCase)
            .Select(os => new GlobalOptionSetSummaryRecord(
                Name: os.Name,
                DisplayName: os.DisplayName?.UserLocalizedLabel?.Label,
                OptionSetType: os.OptionSetType?.ToString() ?? "Unknown",
                OptionCount: os is OptionSetMetadata osm ? osm.Options.Count : 0,
                IsCustomOptionSet: os.IsCustomOptionSet == true))
            .ToList();
    }
}
