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

    /// <summary>
    /// Virtual entity: <c>msdyn_solutioncomponentsummary</c>.
    /// Rich, filterable view of solution components used by the maker portal.
    /// </summary>
    public static class MsdynSolutionComponentSummary
    {
        public const string EntitySetName = "msdyn_solutioncomponentsummaries";
        public const string SolutionId = "msdyn_solutionid";
        public const string ComponentType = "msdyn_componenttype";
        public const string ObjectId = "msdyn_objectid";
        public const string DisplayName = "msdyn_displayname";
        public const string Name = "msdyn_name";
        public const string SchemaName = "msdyn_schemaname";
        public const string IsManaged = "msdyn_ismanaged";
        public const string IsCustomizable = "msdyn_iscustomizable";
        public const string PrimaryEntityName = "msdyn_primaryentityname";
        public const string ComponentTypeName = "msdyn_componenttypename";
        public const string ComponentLogicalName = "msdyn_componentlogicalname";
    }

    /// <summary>
    /// Virtual entity: <c>msdyn_solutioncomponentcountsummary</c>.
    /// Quick per-type component counts within a solution.
    /// </summary>
    public static class MsdynSolutionComponentCountSummary
    {
        public const string EntitySetName = "msdyn_solutioncomponentcountsummaries";
        public const string SolutionId = "msdyn_solutionid";
        public const string ComponentType = "msdyn_componenttype";
        public const string ComponentLogicalName = "msdyn_componentlogicalname";
        public const string Total = "msdyn_total";
    }

    /// <summary>
    /// Virtual entity: <c>msdyn_componentlayer</c>.
    /// Solution layer stack for a given component instance.
    /// </summary>
    public static class MsdynComponentLayer
    {
        public const string EntitySetName = "msdyn_componentlayers";
        public const string ComponentId = "msdyn_componentid";
        public const string SolutionComponentName = "msdyn_solutioncomponentname";
        public const string SolutionName = "msdyn_solutionname";
        public const string PublisherName = "msdyn_publishername";
        public const string Order = "msdyn_order";
        public const string Name = "msdyn_name";
        public const string ComponentJson = "msdyn_componentjson";
        public const string Changes = "msdyn_changes";
        public const string OverwriteTime = "msdyn_overwritetime";
    }
}
