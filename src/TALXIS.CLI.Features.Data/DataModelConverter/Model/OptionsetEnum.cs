using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TALXIS.CLI.Features.Data.DataModelConverter.Model;

public class OptionsetEnum
{
    public string LocalizedName { get; set; }

    public List<OptionsetRow> Values = [];

    public OptionsetEnum(string localizedName, List<OptionsetRow> values)
    {
        LocalizedName = localizedName;
        Values = values;
    }

    public void Add(string label, int value)
    {
        Values.Add(new OptionsetRow(label, value));
    }

    public void MergeOptions(List<OptionsetRow> options)
    {
        foreach (var newoption in options)
        {
            if (!Values.Any(x => x.Value == newoption.Value))
            {
                Values.Add(newoption);
            }
        }
    }

    public override string ToString()
    {
        return LocalizedName;
    }

}
