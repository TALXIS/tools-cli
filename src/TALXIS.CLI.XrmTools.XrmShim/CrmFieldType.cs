namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Describes CRM field types for the <see cref="CrmDataTypeWrapper"/> used
/// by legacy convenience methods on <see cref="CrmServiceClient"/>.
/// </summary>
public enum CrmFieldType
{
    CrmBoolean,
    CrmDateTime,
    CrmDecimal,
    CrmFloat,
    CrmMoney,
    CrmNumber,
    Customer,
    Key,
    Lookup,
    Picklist,
    String,
    UniqueIdentifier,
    Image,
    File
}
