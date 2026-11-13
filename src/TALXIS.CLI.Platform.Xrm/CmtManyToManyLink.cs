namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// One N:N link from the <c>m2mrelationships</c> section of a CMT <c>data.xml</c>.
/// </summary>
public sealed record CmtManyToManyLink(
    string SourceEntity,
    Guid SourceId,
    string TargetEntity,
    Guid TargetId,
    string RelationshipName);
