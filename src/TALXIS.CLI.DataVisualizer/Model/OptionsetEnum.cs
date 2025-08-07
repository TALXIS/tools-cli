using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TALXIS.CLI.DataVisualizer.Model;

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
            OptionsetRow optionsetRow = Values.FirstOrDefault(x => x.Value == newoption.Value);
            if (optionsetRow == default)
            {
                Values.Add(newoption);
            }
            else
            {
                if (optionsetRow.Label != newoption.Label) optionsetRow.Label = $"{newoption.Label}";
            }
        }
    }

    public override string ToString()
    {
        return LocalizedName;
    }

}
