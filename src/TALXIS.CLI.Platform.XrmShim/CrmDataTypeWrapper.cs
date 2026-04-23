namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Wraps a value and its CRM field type for use with the convenience
/// CRUD methods on <see cref="CrmServiceClient"/>.
/// </summary>
public class CrmDataTypeWrapper
{
    public object? Value { get; set; }
    public CrmFieldType Type { get; set; }

    /// <summary>
    /// Name of the entity that a Lookup or Related Customer references.
    /// </summary>
    public string? ReferencedEntity { get; set; }

    public CrmDataTypeWrapper() { }

    public CrmDataTypeWrapper(object? value, CrmFieldType type)
    {
        Value = value;
        Type = type;
    }

    public CrmDataTypeWrapper(object? value, CrmFieldType type, string referencedEntity)
    {
        Value = value;
        Type = type;
        ReferencedEntity = referencedEntity;
    }
}
