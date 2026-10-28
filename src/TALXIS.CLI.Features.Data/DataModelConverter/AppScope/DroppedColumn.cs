namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

public static class DropReason
{
    public const string NoReferenceFound = "no-reference-found";
    public const string PlatformPlumbing = "platform-plumbing";
}

public sealed record DroppedColumn(string Table, string Column, string Reason);
