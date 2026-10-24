using System.IO;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class ColumnReferencesTests
{
    [Fact]
    public void AFileUnderATablesFolder_IsCreditedToThatTable()
    {
        using var tree = new TestTree();
        tree.Write("Model/Declarations/Entities/contoso_order/FormXml/main/form.xml", "<form><cell name=\"contoso_total\" /></form>");

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.True(references.HasOwn("contoso_order"));
        Assert.True(references.OwnedBy("contoso_order", "contoso_total"));
        Assert.False(references.OwnedBy("contoso_customer", "contoso_total"));
        Assert.False(references.Unattributed("contoso_total"));
    }

    [Fact]
    public void AFileOutsideAnyTablesFolder_IsCreditedToNoTable()
    {
        using var tree = new TestTree();
        tree.SiteMap("Model/Declarations", "contoso_app", "contoso_app", "<SubArea Entity=\"contoso_order\" Note=\"contoso_total\" />");

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.True(references.Unattributed("contoso_total"));
        Assert.False(references.HasOwn("contoso_order"));
    }

    [Fact]
    public void CodeFiles_AreReadWhereverTheySit()
    {
        using var tree = new TestTree();
        tree.Write("Plugins/Handler.cs", "entity[\"contoso_fromcode\"] = 1;");
        tree.Write("Controls/index.ts", "const name = 'contoso_fromscript';");

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.True(references.Unattributed("contoso_fromcode"));
        Assert.True(references.Unattributed("contoso_fromscript"));
    }

    [Theory]
    [InlineData("Model")]
    [InlineData("Workflows")]
    public void AnEntityDeclaration_IsNotAReferenceToItsOwnColumns(string above)
    {
        using var tree = new TestTree();
        tree.Write($"{above}/Declarations/Entities/contoso_order/Entity.xml", TestTree.Entity("contoso_order", TestTree.Attr("contoso_declared", "int")));

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.False(references.OwnedBy("contoso_order", "contoso_declared"));
        Assert.False(references.HasOwn("contoso_order"));
    }

    [Fact]
    public void AFileOutsideTheReferencingFolders_IsNotRead()
    {
        using var tree = new TestTree();
        tree.Write("Model/Declarations/Other/Customizations.xml", "<root>contoso_elsewhere</root>");

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.False(references.Unattributed("contoso_elsewhere"));
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    public void ThrowawayDirectories_AreNotEntered(string directory)
    {
        using var tree = new TestTree();
        tree.Write($"Model/{directory}/package/index.js", "const column = 'contoso_ignored';");
        tree.Write("Model/src/index.js", "const column = 'contoso_found';");

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.True(references.Unattributed("contoso_found"));
        Assert.False(references.Unattributed("contoso_ignored"));
    }

    [Fact]
    public void AFolderNamedEntitiesAboveTheRoot_DoesNotChangeWhoseFileItIs()
    {
        using var tree = new TestTree();
        tree.Write("Entities/contoso_wrong/Model/Declarations/Entities/contoso_order/FormXml/form.xml", "<form>contoso_total</form>");

        var references = ColumnReferences.Collect([tree.Full("Entities/contoso_wrong/Model")]);

        Assert.True(references.OwnedBy("contoso_order", "contoso_total"));
        Assert.False(references.HasOwn("contoso_wrong"));
    }

    [Fact]
    public void AReferencingFolderNameAboveTheRoot_DoesNotMakeEveryFileReferencing()
    {
        using var tree = new TestTree();
        tree.Write("FormXml/Model/Other/notes.xml", "<notes>contoso_unrelated</notes>");

        var references = ColumnReferences.Collect([tree.Full("FormXml/Model")]);

        Assert.False(references.Unattributed("contoso_unrelated"));
    }

    [Fact]
    public void ANameIsMatchedWithoutRegardToCase()
    {
        using var tree = new TestTree();
        tree.Write("Model/Declarations/Entities/Contoso_Order/FormXml/form.xml", "<form>Contoso_Total</form>");

        var references = ColumnReferences.Collect([tree.Root]);

        Assert.True(references.OwnedBy("contoso_order", "contoso_total"));
    }

    [Fact]
    public void RootsThatOverlap_ReadAFileOnce()
    {
        using var tree = new TestTree();
        tree.Write("Model/Declarations/Entities/contoso_order/FormXml/form.xml", "<form>contoso_total</form>");

        var references = ColumnReferences.Collect([tree.Root, tree.Full("Model")]);

        Assert.True(references.OwnedBy("contoso_order", "contoso_total"));
    }

    [Fact]
    public void AFileThatCannotBeRead_IsSkippedAndTheOthersStillCount()
    {
        using var tree = new TestTree();
        var locked = tree.Write("Model/Declarations/Entities/contoso_order/FormXml/locked.xml", "<form>contoso_hidden</form>");
        tree.Write("Model/Declarations/Entities/contoso_order/FormXml/open.xml", "<form>contoso_shown</form>");

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var references = ColumnReferences.Collect([tree.Root]);

        Assert.True(references.OwnedBy("contoso_order", "contoso_shown"));
        Assert.False(references.OwnedBy("contoso_order", "contoso_hidden"));
    }
}
