using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TALXIS.CLI.DataVisualizer.Model;

namespace TALXIS.CLI.DataVisualizer.Translators;

public static class EDMXTranslator
{
    public static string ToEDMXNotation(this Table table)
    {
        var result = $"<EntityType Name=\"{table.LogicalName.ToLower()}\"";

        var primaryKey = table.Rows.FirstOrDefault(x => x.RowType == RowType.Primarykey);

        if (primaryKey.Name == "activityid")
        {
            result += " BaseType =\"mscrm.activitypointer\">";
        }
        else
        {
            result += " BaseType =\"mscrm.crmbaseentity\">";
        }

        if (primaryKey != default)
        {
            result += $"<Key><PropertyRef Name=\"{primaryKey.Name.ToLower()}\"/></Key>";
        }

        foreach (var row in table.Rows)
        {
            result += row.ToEDMXNotation();
        }

        return result;

    }

    public static string ToEDMXNotation(this TableRow row)
    {

        string result = "<Property Name=\"{0}\"";

        switch (row.RowType)
        {
            case RowType.Partylist:
            case RowType.Multiselectoptionset:
                result += " Type=\"Edm.String\" Unicode=\"false\"/>";
                break;
            case RowType.Bit:
                result += " Type=\"Edm.Boolean\"/>";
                break;
            case RowType.Managedproperty:
                result += " Type = \"mscrm.BooleanManagedProperty\" />";
                break;
            case RowType.Smallint:
            case RowType.Tinyint:
            case RowType.Int:
            case RowType.State:
            case RowType.Status:
            case RowType.Picklist:
                result += " Type=\"Edm.Int32\"/>";
                break;
            case RowType.Lookup:
            case RowType.Owner:
                result = "<Property Name =\"_{0}_value\" Type=\"Edm.Guid\"/>";
                break;
            case RowType.Customer:
            // Solve
            case RowType.Nvarchar:
            case RowType.Ntext:
                result += " Type=\"Edm.String\" Unicode=\"false\"/>";
                break;
            case RowType.File:
                return $"  <Property Name =\"_{row.Name}_value\" Type=\"Edm.Guid\"/> \n <Property Name =\"{row.Name}_name\" Type=\"Edm.String\" Unicode=\"false\"/>";
            case RowType.Virtual:
                return string.Empty;
            case RowType.Datetimeoffset:
                result += " Type=\"Edm.DateTimeOffset\"/>";
                break;
            case RowType.Date:
                result += " Type=\"Edm.Date\"/>";
                break;
            case RowType.Primarykey:
            case RowType.Uniqueidentifier:
                result += " Type=\"Edm.Guid\"/>";
                break;
            case RowType.Money:
            case RowType.Decimal:
                result += " Type=\"Edm.Decimal\" Scale=\"Variable\"/>";
                break;
            case RowType.Float:
                result += " Type=\"Edm.Double\"/>";
                break;
            case RowType.Long:
            case RowType.Timestamp:
            case RowType.Bigint:
                result += " Type=\"Edm.Int64\"/>";
                break;
            case RowType.Varbinary:
            case RowType.Image:
                result += " Type=\"Edm.Binary\"/>";
                break;
            default:
                break;
        }

        return string.Format(result, row.Name.ToLower());
    }

    public static string ToEDMXNotation(this Relationship relationship, Table table)
    {
        if (relationship.LeftSideTable == table)
        {
            return $"<NavigationProperty Name=\"{relationship.LeftSideRow.Name.ToLower()}\" Type=\"mscrm.{relationship.RighSideTable.LogicalName.ToLower()}\" Nullable=\"false\" Partner=\"{relationship.Name}\"><ReferentialConstraint Property=\"_{relationship.LeftSideRow.Name.ToLower()}_value\" ReferencedProperty=\"{relationship.RighSideRow.Name.ToLower()}\"/></NavigationProperty>";
        }
        else
        {
            return $"<NavigationProperty Name=\"{relationship.Name}\" Type=\"Collection(mscrm.{relationship.LeftSideTable.LogicalName.ToLower()})\" Partner=\"{relationship.LeftSideRow.Name}\"/>";
        }
    }

    public static string ToEDMXNotationBinding(this Relationship relationship, Table table)
    {

        if (relationship.LeftSideTable == table)
        {
            return $"<NavigationPropertyBinding Path=\"{relationship.Name}\" Target=\"{relationship.RighSideTable.SetName.ToLower()}\"/>";
        }
        else
        {
            return $"<NavigationPropertyBinding Path=\"{relationship.Name}\" Target=\"{relationship.LeftSideTable.SetName.ToLower()}\"/>";
        }

    }
}
