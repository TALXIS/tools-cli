using System.Collections.Generic;
using System;

namespace TALXIS.CLI.Component;

public class ComponentOptions
{
    public bool Verbose { get; set; }
    public bool Force { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}
