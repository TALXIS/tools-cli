using System;
using System.Collections.Generic;
using System.Text;

namespace TALXIS.CLI.DataVisualizer.Model;

public class Relationship
{
    private static Dictionary<string, string> CardinalityLookup = new Dictionary<string, string>()
    {
        {"OneToOne", "-"},
        {"OneToMany", ">" },
        {"ManyToOne", "<" }
    };

    public Relationship(string name, string cardinality, Table leftSideTable, TableRow leftSideRow, Table righSideTable, TableRow righSideRow)
    {
        Name = name;
        Cardinality = cardinality;
        LeftSideTable = leftSideTable;
        LeftSideRow = leftSideRow;
        RighSideTable = righSideTable;
        RighSideRow = righSideRow;
    }

    public string Name { get; set; }
    public string Cardinality { get; set; }
    public Table LeftSideTable { get; set; }
    public TableRow LeftSideRow { get; set; }
    public Table RighSideTable { get; set; }
    public TableRow RighSideRow { get; set; }

    public override string ToString()
    {
        return Name;
    }

}
