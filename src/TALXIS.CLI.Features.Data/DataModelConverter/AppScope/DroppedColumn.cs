namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

/// <summary>Why a column was left out, so a reader can tell a judgement from an absence.</summary>
public enum DropReason
{
    /// <summary>Nothing belonging to the column's own table referred to it.</summary>
    NoReferenceFound = 0,

    /// <summary>Platform plumbing a reader never needs, even where something refers
    /// to it.</summary>
    PlatformPlumbing = 1
}

/// <summary>One column left out of the conversion.</summary>
/// <param name="Table">Logical name of the table the column was declared on.</param>
/// <param name="Column">The column's own name.</param>
/// <param name="Reason">Why it was left out.</param>
public sealed record DroppedColumn(string Table, string Column, DropReason Reason)
{
    public override string ToString() => $"{Table}.{Column}";
}
