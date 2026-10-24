using System.Xml.Linq;
using TALXIS.CLI.Features.Workspace.Controls;
using Xunit;

namespace TALXIS.CLI.Tests.Workspace.Controls;

public class FormControlAttachmentServiceTests : IDisposable
{
    private const string SubgridUniqueId = "{bbbb2222-0000-0000-0000-000000000002}";
    private const string ViewId = "{cccc3333-0000-0000-0000-000000000003}";

    private const string FormXml = $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <forms xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <systemform>
            <formid>{aaaa1111-0000-0000-0000-000000000001}</formid>
            <form shownavigationbar="false">
              <tabs>
                <tab id="{aaaa1111-0000-0000-0000-00000000000a}" name="generaltab">
                  <columns><column width="100%"><sections>
                    <section id="{aaaa1111-0000-0000-0000-00000000000b}" name="generalsection">
                      <rows>
                        <row>
                          <cell id="{aaaa1111-0000-0000-0000-00000000000c}">
                            <control indicationOfSubgrid="true" id="subgrid" classid="{E7A81278-8635-4D9E-8D4D-59480B391C5B}" uniqueid="{{SubgridUniqueId}}">
                              <parameters>
                                <TargetEntityType>almlab_warehouseitem</TargetEntityType>
                                <ViewId>{{ViewId}}</ViewId>
                                <ViewIds>{{ViewId}}</ViewIds>
                                <EnableViewPicker>false</EnableViewPicker>
                                <RelationshipName>almlab_location_item</RelationshipName>
                              </parameters>
                            </control>
                          </cell>
                        </row>
                        <row>
                          <cell id="{aaaa1111-0000-0000-0000-00000000000d}">
                            <control id="almlab_name" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" datafieldname="almlab_name" uniqueid="{aaaa1111-0000-0000-0000-00000000000e}" />
                          </cell>
                        </row>
                      </rows>
                    </section>
                  </sections></column></columns>
                </tab>
              </tabs>
              <DisplayConditions Order="1" FallbackForm="true"><Everyone /></DisplayConditions>
              <formLibraries><Library name="almlab_main.js" libraryUniqueId="{aaaa1111-0000-0000-0000-00000000000f}" /></formLibraries>
            </form>
          </systemform>
        </forms>
        """;

    private readonly string _tempDir = Directory.CreateTempSubdirectory("txc-attach-tests").FullName;
    private readonly string _formPath;

    public FormControlAttachmentServiceTests()
    {
        _formPath = Path.Combine(_tempDir, "{aaaa1111-0000-0000-0000-000000000001}.xml");
        File.WriteAllText(_formPath, FormXml);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static ControlManifestInfo GridManifest() => new()
    {
        Namespace = "TALXIS.PCF",
        Constructor = "Grid",
        PrefixedName = "talxis_TALXIS.PCF.Grid",
        DataSets = ["Grid", "RibbonGroupingDataset"],
        Properties =
        [
            new ControlManifestProperty { Name = "Columns", OfType = "Multiple" },
            new ControlManifestProperty { Name = "RowHeight", OfType = "Whole.None", DefaultValue = "42" },
            new ControlManifestProperty { Name = "EnableGrouping", OfType = "Enum", EnumValues = ["true", "false"] },
            new ControlManifestProperty { Name = "ClientApiWebresourceName", OfType = "SingleLine.Text" },
        ],
    };

    private ControlAttachmentRequest Request(
        IReadOnlyDictionary<string, string>? parameters = null,
        string targetControlId = "subgrid",
        bool force = false) => new()
    {
        FormFilePath = _formPath,
        TargetControlId = targetControlId,
        Manifest = GridManifest(),
        ControlName = "talxis_TALXIS.PCF.Grid",
        Parameters = parameters ?? new Dictionary<string, string>(),
        Force = force,
    };

    [Fact]
    public void Attach_InsertsControlDescriptionWithAllFormFactors()
    {
        var result = FormControlAttachmentService.Attach(Request(new Dictionary<string, string>
        {
            ["EnableGrouping"] = "true",
            ["RowHeight"] = "42",
        }));

        Assert.Equal(SubgridUniqueId, result.HostControlUniqueId);
        Assert.False(result.ReplacedExisting);

        var doc = XDocument.Load(_formPath);
        var description = doc.Descendants("controlDescription").Single();
        Assert.Equal(SubgridUniqueId, description.Attribute("forControl")?.Value);

        var customControls = description.Elements("customControl").ToList();
        Assert.Equal(4, customControls.Count);
        Assert.Equal("{E7A81278-8635-4D9E-8D4D-59480B391C5B}", customControls[0].Attribute("id")?.Value);
        Assert.Equal(new[] { "0", "1", "2" }, customControls.Skip(1).Select(c => c.Attribute("formFactor")?.Value));
        Assert.All(customControls.Skip(1), c => Assert.Equal("talxis_TALXIS.PCF.Grid", c.Attribute("name")?.Value));
    }

    [Fact]
    public void Attach_CopiesDatasetBindingFromHostSubgrid()
    {
        FormControlAttachmentService.Attach(Request());

        var doc = XDocument.Load(_formPath);
        var dataSet = doc.Descendants("customControl")
            .First(c => c.Attribute("formFactor")?.Value == "0")
            .Element("parameters")!.Element("data-set")!;

        Assert.Equal("Grid", dataSet.Attribute("name")?.Value);
        Assert.Equal(ViewId, dataSet.Element("ViewId")?.Value);
        Assert.Equal("almlab_warehouseitem", dataSet.Element("TargetEntityType")?.Value);
        Assert.Equal("almlab_location_item", dataSet.Element("RelationshipName")?.Value);
        Assert.Equal(ViewId, dataSet.Element("FilteredViewIds")?.Value);
        Assert.Equal("false", dataSet.Element("IsUserView")?.Value);
    }

    [Fact]
    public void Attach_EmitsTypedParameterElements()
    {
        FormControlAttachmentService.Attach(Request(new Dictionary<string, string>
        {
            ["EnableGrouping"] = "true",
            ["ClientApiWebresourceName"] = "almlab_main.js",
        }));

        var doc = XDocument.Load(_formPath);
        var parameters = doc.Descendants("customControl")
            .First(c => c.Attribute("formFactor")?.Value == "0")
            .Element("parameters")!;

        var grouping = parameters.Element("EnableGrouping")!;
        Assert.Equal("Enum", grouping.Attribute("type")?.Value);
        Assert.Equal("true", grouping.Attribute("static")?.Value);
        Assert.Equal("true", grouping.Value);

        var webresource = parameters.Element("ClientApiWebresourceName")!;
        Assert.Equal("SingleLine.Text", webresource.Attribute("type")?.Value);
        Assert.Equal("almlab_main.js", webresource.Value);
    }

    [Fact]
    public void Attach_InsertsContainerBeforeDisplayConditions()
    {
        FormControlAttachmentService.Attach(Request());

        var doc = XDocument.Load(_formPath);
        var formChildren = doc.Descendants("form").Single().Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["tabs", "controlDescriptions", "DisplayConditions", "formLibraries"], formChildren);
    }

    [Fact]
    public void Attach_UnknownParameter_ThrowsWithValidParameterList()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            FormControlAttachmentService.Attach(Request(new Dictionary<string, string> { ["Nope"] = "1" })));
        Assert.Contains("not a parameter", ex.Message);
        Assert.Contains("EnableGrouping", ex.Message);
    }

    [Fact]
    public void Attach_InvalidEnumValue_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            FormControlAttachmentService.Attach(Request(new Dictionary<string, string> { ["EnableGrouping"] = "banana" })));
        Assert.Contains("Allowed: true, false", ex.Message);
    }

    [Fact]
    public void Attach_NonNumericWholeValue_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            FormControlAttachmentService.Attach(Request(new Dictionary<string, string> { ["RowHeight"] = "tall" })));
        Assert.Contains("whole number", ex.Message);
    }

    [Fact]
    public void Attach_MissingTargetControl_ThrowsListingAvailableControls()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FormControlAttachmentService.Attach(Request(targetControlId: "nosuchgrid")));
        Assert.Contains("subgrid", ex.Message);
    }

    [Fact]
    public void Attach_FieldBoundControl_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FormControlAttachmentService.Attach(Request(targetControlId: "almlab_name")));
        Assert.Contains("not a subgrid", ex.Message);
    }

    [Fact]
    public void Attach_ExistingAttachment_ThrowsWithoutForce()
    {
        FormControlAttachmentService.Attach(Request());

        var ex = Assert.Throws<InvalidOperationException>(() => FormControlAttachmentService.Attach(Request()));
        Assert.Contains("--force", ex.Message);
    }

    [Fact]
    public void Attach_ExistingAttachment_ReplacedWithForce()
    {
        FormControlAttachmentService.Attach(Request(new Dictionary<string, string> { ["EnableGrouping"] = "true" }));
        var result = FormControlAttachmentService.Attach(Request(new Dictionary<string, string> { ["EnableGrouping"] = "false" }, force: true));

        Assert.True(result.ReplacedExisting);
        var doc = XDocument.Load(_formPath);
        Assert.Single(doc.Descendants("controlDescription"));
        var grouping = doc.Descendants("customControl")
            .First(c => c.Attribute("formFactor")?.Value == "0")
            .Element("parameters")!.Element("EnableGrouping")!;
        Assert.Equal("false", grouping.Value);
    }
}
