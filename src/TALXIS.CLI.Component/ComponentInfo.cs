using System;

namespace TALXIS.CLI.Component;

public class ComponentInfo
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Path { get; set; }
    public DateTime? LastModified { get; set; }
}
