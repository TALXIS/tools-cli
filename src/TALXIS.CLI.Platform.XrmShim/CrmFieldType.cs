namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Describes CRM field types for the <see cref="CrmDataTypeWrapper"/> used
/// by legacy convenience methods on <see cref="CrmServiceClient"/>.
/// </summary>
public enum CrmFieldType
{
    CrmBoolean = 0,
    CrmDateTime = 1,
    CrmDecimal = 2,
    CrmFloat = 3,
    CrmMoney = 4,
    CrmNumber = 5,
    Customer = 6,
    Key = 7,
    Lookup = 8,
    Picklist = 9,
    String = 10,
    UniqueIdentifier = 11,
    Image = 12,
    File = 13,
    Raw = 14
}
