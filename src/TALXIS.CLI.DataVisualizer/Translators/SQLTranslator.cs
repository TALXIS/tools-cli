using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TALXIS.CLI.DataVisualizer.Model;

namespace TALXIS.CLI.DataVisualizer.Translators;

public static class SQLTranslator
{
    private static Dictionary<RowType, string> translationTable = new Dictionary<RowType, string>()
        {
            { RowType.Primarykey, "uniqueidentifier PRIMARY KEY"},
            { RowType.Partylist, "nvarchar (1000)"},
        };


    public static string TranslateToSql(RowType key)
    {
        if (translationTable.ContainsKey(key)) return translationTable[key];
        return key.ToString().ToLower();
    }

    public static string ToSQLNotation(this Table table, List<OptionsetEnum> optionsets)
    {
        string result = string.IsNullOrEmpty(table.SetName) ? $"\nCREATE TABLE [{table.LogicalName}] (\n" : $"\nCREATE TABLE [{table.SetName}] (\n";

        List<string> rows = [];
        foreach (var row in table.Rows)
        {
            rows.Add(row.ToSQLNotation(optionsets));
        }

        result += string.Join(",\n", rows.Where(x => !string.IsNullOrEmpty(x)));

        result += "\n)\nGO\n";

        return result;

    }

    public static string ToEDSSQLNotation(this Table table, List<OptionsetEnum> optionsets, List<Relationship> relationships)
    {
        string result = string.IsNullOrEmpty(table.SetName) ? $"\nCREATE TABLE [{table.LogicalName}] (\n" : $"\nCREATE TABLE [{table.SetName}] (\n";

        List<string> rows = [];
        foreach (var row in table.Rows)
        {
            rows.Add(row.ToEDSSQLNotation(optionsets, relationships));
        }

        result += string.Join(",\n", rows.Where(x => !string.IsNullOrEmpty(x)));

        result += "\n)\nGO\n";

        return result;

    }

    public static string ToSQLNotation(this Relationship relationship)
    {

        var cardinality = DBDiagramTranslator.CardinalityLookup[relationship.Cardinality];
        var result = string.Empty;

        if (relationship.RighSideRow.RowType == RowType.Primarykey && relationship.LeftSideRow.RowType == RowType.Primarykey) return string.Empty;

        if (cardinality == ">")
        {

            if (relationship.LeftSideRow.RowType == RowType.Customer)
            {
                result += $"\nALTER TABLE [{(string.IsNullOrEmpty(relationship.LeftSideTable?.SetName) ? relationship.LeftSideTable?.LogicalName : relationship.LeftSideTable?.SetName)}] ADD FOREIGN KEY ([_{relationship.LeftSideRow?.Name}_{relationship.RighSideTable.LogicalName.ToLower()}]) REFERENCES [{(string.IsNullOrEmpty(relationship.RighSideTable?.SetName) ? relationship.RighSideTable?.LogicalName : relationship.RighSideTable?.SetName)}] ([{relationship.RighSideRow?.Name}])";
            }
            else
            {
                result += $"\nALTER TABLE [{(string.IsNullOrEmpty(relationship.LeftSideTable?.SetName) ? relationship.LeftSideTable?.LogicalName : relationship.LeftSideTable?.SetName)}] ADD FOREIGN KEY ([_{relationship.LeftSideRow?.Name}_value]) REFERENCES [{(string.IsNullOrEmpty(relationship.RighSideTable?.SetName) ? relationship.RighSideTable?.LogicalName : relationship.RighSideTable?.SetName)}] ([{relationship.RighSideRow?.Name}])";
            }

        }

        if (cardinality == "<")
        {
            if (relationship.RighSideRow.RowType == RowType.Customer)
            {
                result += $"\nALTER TABLE [{(string.IsNullOrEmpty(relationship.LeftSideTable?.SetName) ? relationship.LeftSideTable?.LogicalName : relationship.LeftSideTable?.SetName)}] ADD FOREIGN KEY ([_{relationship.RighSideRow?.Name}_{relationship.LeftSideTable.LogicalName.ToLower()}]) REFERENCES [{(string.IsNullOrEmpty(relationship.RighSideTable?.SetName) ? relationship.RighSideTable?.LogicalName : relationship.RighSideTable?.SetName)}] ([{relationship.RighSideRow?.Name}])";
            }

            else
            {
                result += $"\nALTER TABLE [{(string.IsNullOrEmpty(relationship.RighSideTable?.SetName) ? relationship.RighSideTable?.LogicalName : relationship.RighSideTable?.SetName)}] ADD FOREIGN KEY ([_{relationship.RighSideRow?.Name}_value]) REFERENCES [{(string.IsNullOrEmpty(relationship.LeftSideTable?.SetName) ? relationship.LeftSideTable?.LogicalName : relationship.LeftSideTable?.SetName)}] ([{relationship.LeftSideRow?.Name}])";

            }
        }

        result += "\nGO\n";

        return result;
    }

    public static string ToEDSSQLNotation(this TableRow row, List<OptionsetEnum> optionsets, List<Relationship> relationships)
    {
        switch (row.RowType)
        {
            case RowType.Multiselectoptionset:
                return $"  [{row.Name}] nvarchar(255)";
            case RowType.Bit:
                return $"  [{row.Name}] bit";
            case RowType.State:
            case RowType.Status:
            case RowType.Picklist:
                var relevantPicklist = optionsets.First(x => x.LocalizedName == row.RowType.ToString());
                return $"  [{row.Name}] nvarchar(255) CHECK ([{row.Name}] IN ({string.Join(',', relevantPicklist.Values.Select(x => "'" + x.Value + "'"))}))";
            case RowType.Managedproperty:
                return $"  [{row.Name}] nvarchar(255) CHECK ([{row.Name}] IN ('0','1'))";
            case RowType.Customer:
                var result = $"  [_{row.Name}_value] nvarchar(255)";

                foreach (var item in relationships.Where(x => x.LeftSideRow.Name == row.Name))
                {
                    result += $",\n [_{row.Name}_{item.RighSideTable.LogicalName.ToLower()}] uniqueidentifier";
                }

                return result;
            case RowType.Lookup:
            case RowType.Owner:
            case RowType.Uniqueidentifier:
                return $"  [_{row.Name}_value] uniqueidentifier";
            case RowType.Nvarchar:
            case RowType.Ntext:
                return $"  [{row.Name}] nvarchar({(row.MaxLenght > 4000 ? "max" : row.MaxLenght.ToString())})";
            case RowType.File:
                return $"  [{row.Name}] uniqueidentifier,\n [{row.Name}_name] nvarchar (max)";
            case RowType.Virtual:
            case RowType.Varbinary:
            case RowType.Timestamp:
                return string.Empty;
            case RowType.Long:
            case RowType.Int:
            case RowType.Smallint:
            case RowType.Tinyint:
                return $"  [{row.Name}] int";
            case RowType.Date:
                return $"  [{row.Name}] datetime";
            case RowType.Datetimeoffset:
                return $"  [{row.Name}] datetimeoffset";
            case RowType.Money:
            case RowType.Decimal:
                return $"  [{row.Name}] decimal";
            case RowType.Primarykey:
            case RowType.Float:
            case RowType.Partylist:
            default:
                return $"  [{row.Name}] {TranslateToSql(row.RowType)}";
        }

    }

    public static string ToSQLNotation(this TableRow row, List<OptionsetEnum> optionsets)
    {
        switch (row.RowType)
        {
            case RowType.Multiselectoptionset:
                return $"  [{row.Name}] nvarchar(255)";
            case RowType.Bit:
                return $"  [{row.Name}] bit";
            case RowType.State:
            case RowType.Status:
            case RowType.Picklist:
                var relevantPicklist = optionsets.First(x => x.LocalizedName == row.RowType.ToString());
                return $"  [{row.Name}] nvarchar(255) CHECK ([{row.Name}] IN ({string.Join(',', relevantPicklist.Values.Select(x => "'" + x.Value + "'"))}))";
            case RowType.Managedproperty:
                return $"  [{row.Name}] nvarchar(255) CHECK ([{row.Name}] IN ('0','1'))";
            case RowType.Lookup:
            case RowType.Owner:
            case RowType.Customer:
                return $"  [{row.Name}] uniqueidentifier";
            case RowType.Nvarchar:
            case RowType.Ntext:
                return $"  [{row.Name}] nvarchar({(row.MaxLenght > 4000 ? "max" : row.MaxLenght.ToString())})";
            case RowType.File:
                return $"  [{row.Name}] uniqueidentifier,\n [{row.Name}_name] nvarchar (max)";
            case RowType.Virtual:
            case RowType.Varbinary:
            case RowType.Timestamp:
                return string.Empty;
            case RowType.Long:
            case RowType.Int:
            case RowType.Smallint:
            case RowType.Tinyint:
                return $"  [{row.Name}] int";
            case RowType.Date:
                return $"  [{row.Name}] datetime";
            case RowType.Datetimeoffset:
                return $"  [{row.Name}] datetimeoffset";
            case RowType.Money:
            case RowType.Decimal:
                return $"  [{row.Name}] decimal";
            case RowType.Primarykey:
            case RowType.Float:
            case RowType.Uniqueidentifier:
            case RowType.Partylist:
            default:
                return $"  [{row.Name}] {TranslateToSql(row.RowType)}";
        }

    }

}
