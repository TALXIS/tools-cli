namespace TALXIS.CLI.Platform.Dataverse.Application;

/// <summary>
/// Logical names of platform-level Dataverse entities that are not tied to a specific domain.
/// Domain-specific entities (e.g. msdyn_solutionhistory, packagehistory) live in their owning project.
/// </summary>
public static class DataverseSchema
{
    public static class Solution
    {
        public const string EntityName = "solution";
    }

    public static class AsyncOperation
    {
        public const string EntityName = "asyncoperation";
    }

    public static class ImportJob
    {
        public const string EntityName = "importjob";
    }
}
