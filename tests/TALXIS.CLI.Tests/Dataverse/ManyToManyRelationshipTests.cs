using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class ManyToManyRelationshipTests
{
    [Fact]
    public void Create_SameTable_SetsReferencingRole()
    {
        var relationship = ManyToManyRelationship.Create("new_item_related", "new_item", "New_Item");

        Assert.Equal("new_item_related", relationship.SchemaName);
        Assert.Equal(EntityRole.Referencing, relationship.PrimaryEntityRole);
    }

    [Fact]
    public void Create_DifferentTables_LeavesRoleEmpty()
    {
        var relationship = ManyToManyRelationship.Create("new_item_contact", "new_item", "contact");

        Assert.Null(relationship.PrimaryEntityRole);
    }
}
