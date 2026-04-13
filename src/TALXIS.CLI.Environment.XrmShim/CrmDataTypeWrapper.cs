namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Wraps a value and its CRM field type for use with the convenience
/// CRUD methods on <see cref="CrmServiceClient"/>.
/// </summary>
public class CrmDataTypeWrapper
{
    public object? Value { get; set; }
    public CrmFieldType Type { get; set; }

    public CrmDataTypeWrapper(object? value, CrmFieldType type)
    {
        Value = value;
        Type = type;
    }
}
