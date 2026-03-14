using System;
using System.Collections.Generic;
using System.Text;

namespace TALXIS.CLI.Data.DataModelConverter.Model;

public class ParsedModel
{
    public List<Table> tables = [];
    public List<Relationship> relationships = [];
    public List<OptionsetEnum> optionSets = [];

}
