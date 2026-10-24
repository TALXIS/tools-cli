using DocumentFormat.OpenXml.Vml.Office;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter.Extensions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data.DataModelConverter.Model;


public enum TableType
{
    InSolution = 0,
    NotInSolution = 1,
    ConnectionTable = 2,

    /// <summary>A stub for a table an input declares but app scoping left out.</summary>
    NotInApp = 3
}

public class Table
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(Table));

    public Table() { }

    public Table(XElement element)
    {
        LocalizedName = element.Elements("Name").FirstOrDefault(x => x.Name == "Name").Attribute("LocalizedName").Value.Replace(" ", "_").NormalizeString();
        LogicalName = element.Element("Name")?.Value;
        SetName = element.Elements("EntityInfo").Elements("entity").Elements("EntitySetName").ToList().Count != 0 ? element.Elements("EntityInfo").Elements("entity").Elements("EntitySetName").FirstOrDefault().Value : string.Empty;
    }

    public string LocalizedName { get; set; }
    public string LogicalName { get; set; }
    public string SetName { get; set; }
    [JsonIgnore]
    public Module ParentModule { get; set; }
    public RibbonDiffXml ribbonDiff { get; set; }
    public List<TableRow> Rows = [];
    public TableType Type { get; set; }

    public TableRow GetOrCreateRow(string rowName, RowType rowType, int maxLength = 0, string optionsetname = "")
    {
        var row = Rows.FirstOrDefault(x => string.Compare(x.Name, rowName, true) == 0);
        if (row == null)
        {
            var tableRow = new TableRow(rowName, rowType);

            if (maxLength > 0) tableRow.MaxLenght = maxLength;
            if (!string.IsNullOrEmpty(optionsetname)) tableRow.OptionSetName = optionsetname;

            Rows.Add(tableRow);
        }
        return Rows.FirstOrDefault(x => string.Compare(x.Name, rowName, true) == 0);
    }

    public void ParseMultipleRowsFromXml(List<XElement> xElements)
    {
        foreach (var element in xElements)
        {
            var row = TableRow.ParseXElement(element);
            if (row == null)
                continue;

            var existing = Rows.FirstOrDefault(x => string.Equals(x.Name, row.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                Rows.Add(row);
                continue;
            }

            existing.IsLogical ??= row.IsLogical;

            if (existing.RowType != row.RowType)
            {
                _logger.LogWarning(
                    "Attribute {Table}.{Attribute} is declared as {ExistingType} and again as {NewType}; keeping the first.",
                    LogicalName, row.Name, existing.RowType, row.RowType);
            }
            else if (row.MaxLenght > existing.MaxLenght)
            {
                existing.MaxLenght = row.MaxLenght;
            }
        }
    }

    public void ParseRibbonDiffXml(XElement ribbonDiffElement)
    {
        var serializer = new XmlSerializer(typeof(RibbonDiffXml));
        using var reader = ribbonDiffElement.CreateReader();
        var root = serializer.Deserialize(reader) as RibbonDiffXml ?? throw new InvalidOperationException("Failed to deserialize RibbonDiffXml.");

        if (ribbonDiff != null)
        {
            root.RemoveDeclaredIn(ribbonDiff);
            ribbonDiff.Merge(root);
        }
        else
        {
            ribbonDiff = root;
        }
    }

    public override string ToString()
    {
        return LogicalName;
    }
}
