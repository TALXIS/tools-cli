using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

namespace TALXIS.CLI.Features.Data;

public sealed record DataModelConvertResult(
    string Status,
    string Message,
    string OutputFile,
    string Detail,
    int? ColumnsDropped,
    IReadOnlyList<DroppedReasonCount>? DroppedByReason,
    IReadOnlyList<DroppedColumn>? DroppedColumns)
{
    public static DataModelConvertResult For(string outputFile, DetailLevel detail, IReadOnlyList<DroppedColumn> dropped, bool listDropped)
    {
        var minimal = detail == DetailLevel.Minimal;

        IReadOnlyList<DroppedReasonCount>? byReason = null;
        if (minimal)
        {
            byReason = [.. dropped
                .GroupBy(column => column.Reason)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DroppedReasonCount(group.Key, group.Count()))];
        }

        return new DataModelConvertResult(
            "succeeded",
            $"Output written to: {outputFile}",
            outputFile,
            detail.ToString().ToLowerInvariant(),
            minimal ? dropped.Count : null,
            byReason,
            minimal && listDropped ? dropped : null);
    }
}

public sealed record DroppedReasonCount(string Reason, int Count);
