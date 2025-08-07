using System;
using System.Collections.Generic;
using System.Text;
using TALXIS.CLI.DataVisualizer.Model;

namespace TALXIS.CLI.DataVisualizer.Translators;

public static class DBDiagramTranslator
{

    public static Dictionary<string, string> CardinalityLookup = new Dictionary<string, string>()
        {
            {"OneToOne", "-"},
            {"OneToMany", ">" },
            {"ManyToOne", "<" }
        };

    public static string ToDbDiagramNotation(this Table table)
    {
        var result = $"\ntable {table.LogicalName} ";

        switch (table.Type)
        {
            case TableType.InSolution:
                result += $"[headercolor: {table.ParentModule?.Colorhex}] //{table.ParentModule?.ModuleName} \n";
                break;
            case TableType.NotInSolution:
                result += "[headercolor: #c0392b] ";
                break;
            case TableType.ConnectionTable:
                result += "[headercolor: #27ae60] ";
                break;
            default:
                break;
        }

        result += "{\n";

        foreach (var row in table.Rows)
        {
            result += row.ToDbDiagramNotation();
        }

        result += "}";

        return result;

    }

    public static string ToDbDiagramNotation(this TableRow row)
    {
        return $"  {row.Name} {row.RowType} \n";


    }

    public static string ToDbDiagramNotation(this Relationship relationship)
    {
        return $"\nRef: \"{relationship.LeftSideTable?.LogicalName}\".\"{relationship.LeftSideRow?.Name}\" {CardinalityLookup[relationship.Cardinality]} \"{relationship.RighSideTable?.LogicalName}\".\"{relationship.RighSideRow?.Name}\"";

    }

    public static string ToDbDiagramNotation(this OptionsetRow row)
    {
        return $"\"{row.Value}\" [note:'{row.Label}']";
    }

    public static string ToDbDiagramNotation(this OptionsetEnum optionset)
    {
        var result = $"\nEnum {optionset.LocalizedName} {{";

        foreach (var value in optionset.Values)
        {
            result += $"\n {value.ToDbDiagramNotation()}";
        }

        result += "\n}";

        return result;

    }
}

