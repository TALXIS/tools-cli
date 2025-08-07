using System;
using System.Collections.Generic;
using System.Text;

namespace TALXIS.CLI.DataVisualizer.Model;

public class OptionsetRow
{
    public OptionsetRow(string label, int value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; set; }
    public int Value { get; set; }


}
