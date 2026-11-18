using System;
using System.Collections.Generic;
using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Tests.TemplateEngine;

#pragma warning disable CS0618 // Documentation and Priority are [Obsolete] but still declared by ITemplateParameter
internal sealed class FakeTemplateParameter : ITemplateParameter
{
    public FakeTemplateParameter(string name, bool isName = false, string dataType = "text")
    {
        Name = name;
        IsName = isName;
        DataType = dataType;
    }

    public string? Documentation => Description;
    public string? Description { get; init; }
    public string Name { get; }
    public TemplateParameterPriority Priority => TemplateParameterPriority.Optional;
    public TemplateParameterPrecedence Precedence { get; init; } = TemplateParameterPrecedence.Default;
    public string Type => "parameter";
    public bool IsName { get; }
    public string? DefaultValue { get; init; }
    public string DataType { get; }
    public IReadOnlyDictionary<string, ParameterChoice>? Choices { get; init; }
    public string? DisplayName { get; init; }
    public bool AllowMultipleValues => false;
    public string? DefaultIfOptionWithoutValue => null;

    public bool Equals(ITemplateParameter? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ITemplateParameter other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);
}
#pragma warning restore CS0618
