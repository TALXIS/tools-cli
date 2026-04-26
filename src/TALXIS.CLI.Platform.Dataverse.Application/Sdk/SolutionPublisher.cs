using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Publishes customizations in Dataverse.
/// </summary>
internal static class SolutionPublisher
{
    public static async Task PublishAsync(
        IOrganizationServiceAsync2 service,
        IReadOnlyList<string>? entityLogicalNames,
        CancellationToken ct)
    {
        if (entityLogicalNames is null || entityLogicalNames.Count == 0)
        {
            await service.ExecuteAsync(new PublishAllXmlRequest(), ct).ConfigureAwait(false);
            return;
        }

        // Selective publish: build ParameterXml for specific entities
        var xml = BuildPublishXml(entityLogicalNames);
        var request = new PublishXmlRequest { ParameterXml = xml };
        await service.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    private static string BuildPublishXml(IReadOnlyList<string> entityLogicalNames)
    {
        var root = new XElement("importexportxml",
            new XElement("entities",
                entityLogicalNames.Select(name => new XElement("entity", name))));
        return root.ToString(SaveOptions.DisableFormatting);
    }
}
