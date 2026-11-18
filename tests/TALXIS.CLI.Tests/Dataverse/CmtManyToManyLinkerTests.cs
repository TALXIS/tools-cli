using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Platform.Xrm;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class CmtManyToManyLinkerTests
{
    private const string DataXml = """
        <entities>
          <entity name="new_item" displayname="Item">
            <records />
            <m2mrelationships>
              <m2mrelationship sourceid="11111111-0000-0000-0000-000000000001" targetentityname="new_tag" targetentitynameidfield="new_tagid" m2mrelationshipname="new_item_new_tag">
                <targetids>
                  <targetid>22222222-0000-0000-0000-000000000001</targetid>
                  <targetid>22222222-0000-0000-0000-000000000002</targetid>
                </targetids>
              </m2mrelationship>
              <m2mrelationship sourceid="11111111-0000-0000-0000-000000000002" targetentityname="new_item" targetentitynameidfield="new_itemid" m2mrelationshipname="new_item_related">
                <targetids>
                  <targetid>11111111-0000-0000-0000-000000000001</targetid>
                </targetids>
              </m2mrelationship>
            </m2mrelationships>
          </entity>
          <entity name="new_tag" displayname="Tag">
            <records />
            <m2mrelationships />
          </entity>
        </entities>
        """;

    [Fact]
    public void ReadLinks_ReturnsOneLinkPerTarget()
    {
        var links = CmtManyToManyLinker.ReadLinks(WriteDataXml());

        Assert.Equal(3, links.Count);
        Assert.Equal(new CmtManyToManyLink("new_item", Guid.Parse("11111111-0000-0000-0000-000000000001"), "new_tag", Guid.Parse("22222222-0000-0000-0000-000000000002"), "new_item_new_tag"), links[1]);
    }

    [Fact]
    public void Link_CountsDuplicatesAsAlreadyLinkedAndSetsReflexiveRole()
    {
        var service = new FakeOrganizationService(failIndex: 0, failCode: unchecked((int)0x80040237));

        var result = CmtManyToManyLinker.Link(service, CmtManyToManyLinker.ReadLinks(WriteDataXml()), 600);

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.AlreadyLinked);
        Assert.Empty(result.Errors);
        Assert.Equal("new_item_new_tag_schema", service.Associations[0].Relationship.SchemaName);
        Assert.Null(service.Associations[0].Relationship.PrimaryEntityRole);
        Assert.Equal(EntityRole.Referencing, service.Associations[2].Relationship.PrimaryEntityRole);
    }

    [Fact]
    public void Link_ReportsOtherFaultsAsErrors()
    {
        var service = new FakeOrganizationService(failIndex: 1, failCode: unchecked((int)0x80040217));

        var result = CmtManyToManyLinker.Link(service, CmtManyToManyLinker.ReadLinks(WriteDataXml()), 600);

        Assert.Equal(2, result.Created);
        var error = Assert.Single(result.Errors);
        Assert.Contains("22222222-0000-0000-0000-000000000002", error);
        Assert.Contains("Does Not Exist", error);
    }

    private static string WriteDataXml()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cmt-m2m-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, DataXml);
        return path;
    }

    private sealed class FakeOrganizationService(int failIndex, int failCode) : IOrganizationService
    {
        public List<AssociateRequest> Associations { get; } = new();

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveEntityRequest retrieve)
            {
                var metadata = new EntityMetadata { LogicalName = retrieve.LogicalName };
                var relationship = new ManyToManyRelationshipMetadata { SchemaName = "new_item_new_tag_schema", IntersectEntityName = "new_item_new_tag" };
                typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.ManyToManyRelationships))!
                    .SetValue(metadata, new[] { relationship });
                return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = metadata } };
            }

            var executeMultiple = (ExecuteMultipleRequest)request;
            var responses = new ExecuteMultipleResponseItemCollection();
            for (var i = 0; i < executeMultiple.Requests.Count; i++)
            {
                Associations.Add((AssociateRequest)executeMultiple.Requests[i]);
                if (i == failIndex)
                {
                    responses.Add(new ExecuteMultipleResponseItem
                    {
                        RequestIndex = i,
                        Fault = new OrganizationServiceFault { ErrorCode = failCode, Message = "new_tag With Id Does Not Exist" }
                    });
                }
            }

            return new ExecuteMultipleResponse { Results = { ["Responses"] = responses, ["IsFaulted"] = true } };
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
    }
}
