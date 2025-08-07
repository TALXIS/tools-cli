using System;
using System.Collections.Generic;
using System.Linq;
using TALXIS.CLI.DataVisualizer.Model;

namespace TALXIS.CLI.DataVisualizer.Extensions;

public static class TableExtension
{
    public static Table Find(this List<Table> list, string logicalName)
    {
        return list.FirstOrDefault(x => x.LogicalName.Equals(logicalName, StringComparison.InvariantCultureIgnoreCase));
    }

    public static Table CreateTable(string tableName, TableType type)
    {
        return new Table
        {
            Type = type,
            LocalizedName = tableName,
            LogicalName = tableName,
            SetName = tableName + "s",
            Rows = { new TableRow(tableName + "id", RowType.Primarykey) }
        };
    }
}

