namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Filter applied when listing Dataverse users or service principals.
/// </summary>
public enum DataverseSecurityPrincipalStateFilter
{
    Enabled = 0,
    Disabled = 1,
    All = 2,
}

/// <summary>
/// Dataverse team types supported by the environment-team service layer.
/// </summary>
public enum DataverseTeamType
{
    Owner = 0,
    Access = 1,
    AadSecurityGroup = 2,
    AadOfficeGroup = 3,
}

/// <summary>
/// Membership projection used by Azure AD-backed Dataverse teams.
/// </summary>
public enum DataverseTeamMembershipType
{
    MembersAndGuests = 0,
    Members = 1,
    Owners = 2,
    Guests = 3,
}

/// <summary>
/// Candidate returned when a friendly-identifier lookup matches more than one
/// Dataverse record.
/// </summary>
public sealed record DataverseLookupCandidate(
    Guid Id,
    string Name,
    string? Description);

/// <summary>
/// Raised when a friendly identifier such as a role name, team name or user
/// principal name matches more than one Dataverse record.
/// </summary>
public sealed class DataverseAmbiguousMatchException : InvalidOperationException
{
    public DataverseAmbiguousMatchException(
        string entityDisplayName,
        string identifier,
        IReadOnlyList<DataverseLookupCandidate> candidates)
        : base(BuildMessage(entityDisplayName, identifier, candidates))
    {
        EntityDisplayName = entityDisplayName;
        Identifier = identifier;
        Candidates = candidates;
    }

    /// <summary>
    /// Gets the human-readable Dataverse entity label used in the lookup.
    /// </summary>
    public string EntityDisplayName { get; }

    /// <summary>
    /// Gets the original identifier supplied by the caller.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets the records that matched the supplied identifier.
    /// </summary>
    public IReadOnlyList<DataverseLookupCandidate> Candidates { get; }

    private static string BuildMessage(
        string entityDisplayName,
        string identifier,
        IReadOnlyList<DataverseLookupCandidate> candidates)
    {
        var formattedCandidates = string.Join(", ", candidates.Select(static candidate =>
            candidate.Description is null
                ? $"{candidate.Name} ({candidate.Id})"
                : $"{candidate.Name} [{candidate.Description}] ({candidate.Id})"));

        return $"Multiple {entityDisplayName} records matched '{identifier}'. Candidates: {formattedCandidates}";
    }
}

/// <summary>
/// Read model for a Dataverse environment user (<c>systemuser</c> with a null
/// <c>applicationid</c>).
/// </summary>
public sealed record DataverseUserRecord(
    Guid Id,
    string? FullName,
    string? UserPrincipalName,
    string? PrimaryEmailAddress,
    Guid? AzureActiveDirectoryObjectId,
    bool IsDisabled,
    Guid? BusinessUnitId,
    string? BusinessUnitName);

/// <summary>
/// Read model for a Dataverse service principal (<c>systemuser</c> with a
/// populated <c>applicationid</c>).
/// </summary>
public sealed record DataverseServicePrincipalRecord(
    Guid Id,
    Guid ApplicationId,
    string? FullName,
    Guid? AzureActiveDirectoryObjectId,
    bool IsDisabled,
    Guid? BusinessUnitId,
    string? BusinessUnitName);

/// <summary>
/// Read model for a Dataverse security role.
/// </summary>
public sealed record DataverseRoleRecord(
    Guid Id,
    string Name,
    Guid? BusinessUnitId,
    string? BusinessUnitName);

/// <summary>
/// Read model for a Dataverse team.
/// </summary>
public sealed record DataverseTeamRecord(
    Guid Id,
    string Name,
    DataverseTeamType TeamType,
    Guid? AzureActiveDirectoryObjectId,
    DataverseTeamMembershipType? MembershipType,
    Guid? BusinessUnitId,
    string? BusinessUnitName,
    bool IsDefault,
    bool IsSystemManaged);

/// <summary>
/// Input model for creating a Dataverse service principal.
/// </summary>
public sealed record DataverseServicePrincipalCreateOptions(
    Guid EntraClientId,
    string? BusinessUnitIdOrName,
    IReadOnlyList<string> InitialRoleNamesOrGuids);

/// <summary>
/// Input model for creating a Dataverse team.
/// </summary>
public sealed record DataverseTeamCreateOptions(
    string Name,
    DataverseTeamType TeamType,
    Guid? AadObjectId,
    DataverseTeamMembershipType? MembershipType,
    string? BusinessUnitIdOrName);
