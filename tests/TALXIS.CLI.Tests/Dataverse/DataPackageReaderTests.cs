using System.IO;
using System.IO.Compression;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

/// <summary>
/// Pure-parser tests for <see cref="DataPackageReader"/>. Uses tiny synthetic
/// data.xml + data_schema.xml fixtures so the tests don't depend on a live
/// environment.
/// </summary>
public class DataPackageReaderTests : IDisposable
{
    private readonly string _tempRoot;

    public DataPackageReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "txc-tests", "data-package-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Load_FromFolder_ParsesEntityOrderAndSchemaAndRecords()
    {
        var folder = WriteFixture("pkg-folder");

        var contents = DataPackageReader.Load(folder);

        Assert.Equal(new[] { "account", "contact" }, contents.EntityImportOrder);

        Assert.True(contents.Schemas.ContainsKey("account"));
        var accountSchema = contents.Schemas["account"];
        Assert.Equal("accountid", accountSchema.PrimaryIdField);
        Assert.Equal("name", accountSchema.PrimaryNameField);
        Assert.Contains("name", accountSchema.NaturalKeyFields);
        Assert.Contains("accountnumber", accountSchema.NaturalKeyFields);

        Assert.True(contents.Records.ContainsKey("account"));
        Assert.Equal(2, contents.Records["account"].Count);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), contents.Records["account"][0].Id);
        Assert.Equal("Contoso", contents.Records["account"][0].Fields["name"]);
    }

    [Fact]
    public void Load_FromZip_ParsesSameAsFolder()
    {
        var folder = WriteFixture("pkg-zip-source");
        var zipPath = Path.Combine(_tempRoot, "package.zip");
        ZipFile.CreateFromDirectory(folder, zipPath);

        var contents = DataPackageReader.Load(zipPath);

        Assert.Equal(new[] { "account", "contact" }, contents.EntityImportOrder);
        Assert.Equal(2, contents.Records["account"].Count);
    }

    [Fact]
    public void Load_FolderMissingFiles_Throws()
    {
        var folder = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(folder);

        Assert.Throws<InvalidOperationException>(() => DataPackageReader.Load(folder));
    }

    [Fact]
    public void Load_NonexistentPath_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DataPackageReader.Load(Path.Combine(_tempRoot, "nope")));
    }

    [Fact]
    public void Load_M2mAssociations_AreResolvedAgainstSchema()
    {
        var folder = WriteFixture("pkg-m2m");
        var contents = DataPackageReader.Load(folder);

        Assert.Single(contents.M2mAssociations);
        var assoc = contents.M2mAssociations[0];
        Assert.Equal("account_lead_association", assoc.RelationshipName);
        Assert.Equal("account", assoc.SourceEntity);
        Assert.Equal("lead", assoc.TargetEntity);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), assoc.SourceId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), assoc.TargetId);
    }

    private string WriteFixture(string folderName)
    {
        var folder = Path.Combine(_tempRoot, folderName);
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "data_schema.xml"), SchemaXml);
        File.WriteAllText(Path.Combine(folder, "data.xml"), DataXml);

        return folder;
    }

    private const string SchemaXml = """
<?xml version="1.0" encoding="utf-8"?>
<entities>
  <entityImportOrder>
    <entityName>account</entityName>
    <entityName>contact</entityName>
  </entityImportOrder>
  <entity name="account" displayname="Account" etc="1"
          primaryidfield="accountid" primarynamefield="name">
    <fields>
      <field name="accountid" type="guid" primaryKey="true" />
      <field name="name" type="string" />
      <field name="accountnumber" type="string" updateCompare="true" />
    </fields>
    <relationships>
      <relationship name="account_lead_association"
                    manyToMany="true"
                    m2mTargetEntity="lead"
                    m2mTargetEntityPrimaryKey="leadid" />
    </relationships>
  </entity>
  <entity name="contact" displayname="Contact" etc="2"
          primaryidfield="contactid" primarynamefield="fullname">
    <fields>
      <field name="contactid" type="guid" primaryKey="true" />
      <field name="fullname" type="string" />
    </fields>
  </entity>
</entities>
""";

    private const string DataXml = """
<?xml version="1.0" encoding="utf-8"?>
<entities timestamp="2024-01-01T00:00:00Z">
  <entity name="account" displayname="Account">
    <records>
      <record id="11111111-1111-1111-1111-111111111111">
        <field name="accountid" value="11111111-1111-1111-1111-111111111111" />
        <field name="name" value="Contoso" />
        <field name="accountnumber" value="ACC-001" />
      </record>
      <record id="33333333-3333-3333-3333-333333333333">
        <field name="accountid" value="33333333-3333-3333-3333-333333333333" />
        <field name="name" value="Initech" />
        <field name="accountnumber" value="ACC-002" />
      </record>
    </records>
    <m2mrelationships>
      <m2mrelationship m2mrelationshipname="account_lead_association"
                       sourceid="11111111-1111-1111-1111-111111111111">
        <targetids>
          <targetid>22222222-2222-2222-2222-222222222222</targetid>
        </targetids>
      </m2mrelationship>
    </m2mrelationships>
  </entity>
  <entity name="contact" displayname="Contact">
    <records>
      <record id="44444444-4444-4444-4444-444444444444">
        <field name="contactid" value="44444444-4444-4444-4444-444444444444" />
        <field name="fullname" value="John Smith" />
      </record>
    </records>
    <m2mrelationships />
  </entity>
</entities>
""";
}
