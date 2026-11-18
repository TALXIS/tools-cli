using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Links the N:N rows of a CMT data package after a batch-mode import.
/// </summary>
/// <remarks>
/// In batch mode CMT only records the IDs of rows it created, so its own N:N pass
/// skips every link whose source row already existed and still reports success.
/// </remarks>
public static class CmtManyToManyLinker
{
    private const int DuplicateRecordErrorCode = unchecked((int)0x80040237);

    public static IReadOnlyList<CmtManyToManyLink> ReadLinks(string dataXmlPath)
    {
        var document = XDocument.Load(dataXmlPath);
        var links = new List<CmtManyToManyLink>();

        foreach (var entity in document.Root?.Elements("entity") ?? [])
        {
            var sourceEntity = (string?)entity.Attribute("name");
            if (string.IsNullOrEmpty(sourceEntity)) continue;

            foreach (var relationship in entity.Element("m2mrelationships")?.Elements("m2mrelationship") ?? [])
            {
                var sourceId = Guid.Parse((string)relationship.Attribute("sourceid")!);
                var targetEntity = (string)relationship.Attribute("targetentityname")!;
                var relationshipName = (string)relationship.Attribute("m2mrelationshipname")!;

                foreach (var targetId in relationship.Element("targetids")?.Elements("targetid") ?? [])
                {
                    links.Add(new CmtManyToManyLink(sourceEntity, sourceId, targetEntity, Guid.Parse(targetId.Value), relationshipName));
                }
            }
        }

        return links;
    }

    public static CmtManyToManyLinkResult Link(IOrganizationService service, IReadOnlyList<CmtManyToManyLink> links, int batchSize)
    {
        var schemaNames = new Dictionary<(string Entity, string Name), string>();
        var created = 0;
        var alreadyLinked = 0;
        var errors = new List<string>();

        foreach (var chunk in links.Chunk(Math.Clamp(batchSize, 1, 1000)))
        {
            var request = new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = false },
                Requests = new OrganizationRequestCollection()
            };

            foreach (var link in chunk)
            {
                var key = (link.SourceEntity, link.RelationshipName);
                if (!schemaNames.TryGetValue(key, out var schemaName))
                {
                    schemaName = ResolveSchemaName(service, link.SourceEntity, link.RelationshipName);
                    schemaNames[key] = schemaName;
                }

                var relationship = new Relationship(schemaName);
                if (string.Equals(link.SourceEntity, link.TargetEntity, StringComparison.OrdinalIgnoreCase)) relationship.PrimaryEntityRole = EntityRole.Referencing;

                request.Requests.Add(new AssociateRequest
                {
                    Target = new EntityReference(link.SourceEntity, link.SourceId),
                    RelatedEntities = new EntityReferenceCollection { new EntityReference(link.TargetEntity, link.TargetId) },
                    Relationship = relationship
                });
            }

            var response = (ExecuteMultipleResponse)service.Execute(request);
            var faults = response.Responses.Where(r => r.Fault is not null).ToDictionary(r => r.RequestIndex, r => r.Fault);

            for (var i = 0; i < chunk.Length; i++)
            {
                if (!faults.TryGetValue(i, out var fault))
                {
                    created++;
                }
                else if (fault.ErrorCode == DuplicateRecordErrorCode)
                {
                    alreadyLinked++;
                }
                else
                {
                    var link = chunk[i];
                    errors.Add($"{link.SourceEntity}/{link.SourceId} -> {link.TargetEntity}/{link.TargetId} via '{link.RelationshipName}': {fault.Message}");
                }
            }
        }

        return new CmtManyToManyLinkResult(created, alreadyLinked, errors);
    }

    private static string ResolveSchemaName(IOrganizationService service, string entityName, string relationshipName)
    {
        var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = entityName,
            EntityFilters = EntityFilters.Relationships
        });

        // data.xml names the relationship by its intersect table, which can differ from the schema name AssociateRequest needs.
        var match = response.EntityMetadata.ManyToManyRelationships.FirstOrDefault(r =>
            string.Equals(r.IntersectEntityName, relationshipName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.SchemaName, relationshipName, StringComparison.OrdinalIgnoreCase));

        return match?.SchemaName ?? relationshipName;
    }
}
