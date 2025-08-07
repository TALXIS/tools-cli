using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using TALXIS.CLI.DataVisualizer.Extensions;

namespace TALXIS.CLI.DataVisualizer.Model;


public class TableRow
{
    public TableRow(string name, RowType rowType)
    {
        Name = name;
        RowType = rowType;
    }

    public string Name { get; set; }
    public int MaxLenght { get; set; }
    public string OptionSetName { get; set; }
    public RowType RowType { get; set; }

    internal static TableRow ParseXElement(XElement attribute)
    {
        string optionsetName = string.Empty;
        RowType rowType;
        int maxLength = 0;
        switch (attribute.Elements("Type")?.FirstOrDefault()?.Value)
        {
            case "bit":
                rowType = RowType.Bit;
                optionsetName = attribute.Element("OptionSetName") == null ? attribute.Element("optionset") == null ? attribute.Elements("Type").FirstOrDefault().Value : attribute.Element("optionset").Attribute("Name").Value : attribute.Element("OptionSetName").Value;
                break;
            case "multiselectpicklist":
                rowType = RowType.Multiselectoptionset;
                optionsetName = attribute.Element("OptionSetName") == null ? attribute.Element("optionset") == null ? attribute.Elements("Type").FirstOrDefault().Value : attribute.Element("optionset").Attribute("Name").Value : attribute.Element("OptionSetName").Value;
                break;
            case "picklist":
                rowType = RowType.Picklist;
                optionsetName = attribute.Element("OptionSetName") == null ? attribute.Element("optionset") == null ? attribute.Elements("Type").FirstOrDefault().Value : attribute.Element("optionset").Attribute("Name").Value : attribute.Element("OptionSetName").Value;
                break;
            case "state":
                rowType = RowType.State;
                optionsetName = attribute.Element("OptionSetName") == null ? attribute.Element("optionset") == null ? attribute.Elements("Type").FirstOrDefault().Value : attribute.Element("optionset").Attribute("Name").Value : attribute.Element("OptionSetName").Value;
                break;
            case "status":
                rowType = RowType.Status;
                optionsetName = attribute.Element("OptionSetName") == null ? attribute.Element("optionset") == null ? attribute.Elements("Type").FirstOrDefault().Value : attribute.Element("optionset").Attribute("Name").Value : attribute.Element("OptionSetName").Value;
                break;
            case "virtual": //in case of default entities
                rowType = RowType.Virtual;
                break;
            case "datetime":
                if (attribute.Elements("Behavior").FirstOrDefault()?.Value == "2" || attribute.Elements("Behavior").FirstOrDefault()?.Value.ToLower() == "dateonly")
                {
                    rowType = RowType.Date;
                }
                else
                {
                    rowType = RowType.Datetimeoffset;
                }
                break;
            case "primarykey":
                rowType = RowType.Primarykey;
                break;
            case "lookup":
                rowType = RowType.Lookup;
                break;
            case "uniqueidentifier":
                rowType = RowType.Uniqueidentifier;
                break;
            case "owner":
                rowType = RowType.Owner;
                break;
            case "nvarchar":
                rowType = RowType.Nvarchar;

                if (attribute.Elements("MaxLength").FirstOrDefault() != default)
                {
                    maxLength = int.Parse(attribute.Elements("MaxLength").FirstOrDefault()?.Value ?? "0");
                }
                else if (attribute.Elements("Length").FirstOrDefault() != default)
                {
                    maxLength = int.Parse(attribute.Elements("Length").FirstOrDefault()?.Value ?? "0");
                }

                break;
            case "ntext":
                rowType = RowType.Ntext;
                if (attribute.Elements("MaxLength").FirstOrDefault() != default)
                {
                    maxLength = int.Parse(attribute.Elements("MaxLength").FirstOrDefault()?.Value ?? "0");
                }
                break;
            default:
                rowType = Enum.Parse<RowType>(attribute.Elements("Type")?.FirstOrDefault()?.Value.FirstCharToUpper());
                break;

        }

        return new TableRow(attribute.Attribute("PhysicalName").Value.ToLower(), rowType)
        {
            MaxLenght = maxLength,
            OptionSetName = optionsetName
        };

    }

    public override string ToString()
    {
        return Name;
    }
}
