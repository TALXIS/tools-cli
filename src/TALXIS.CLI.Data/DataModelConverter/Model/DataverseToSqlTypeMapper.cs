using System.Collections.Generic;

namespace TALXIS.CLI.Data.DataModelConverter.Model;

public class DataverseToSqlTypeMapper
{
    private readonly Dictionary<string, string> translationTable = new Dictionary<string, string>()
    {
        { "nvarchar", "varchar" },
        { "lookup", "uniqueidentifier" },
        { "primarykey", "uniqueidentifier [primary key]"},
        { "partylist", "varchar"},
        { "file", "varchar" },
        { "customer", "uniqueidentifier" },
        { "ntext", "varchar" }
    };

    public string this[string key]
    {
        get
        {
            if (translationTable.ContainsKey(key)) return translationTable[key];
            return key;
        }
    }

}
