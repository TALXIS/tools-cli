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
        public const string AsyncOperationId = "asyncoperationid";
        public const string Name = "name";
        public const string OperationType = "operationtype";
        public const string StateCode = "statecode";
        public const string StatusCode = "statuscode";
        public const string Message = "message";
        public const string FriendlyMessage = "friendlymessage";
        public const string RegardingObjectId = "regardingobjectid";
        public const string CreatedOn = "createdon";
        public const string StartedOn = "startedon";
        public const string CompletedOn = "completedon";
        public const string CorrelationId = "correlationid";
        public const string ErrorCode = "errorcode";

        /// <summary>Option-set value of <c>operationtype</c> for a classic workflow run.</summary>
        public const int OperationTypeWorkflow = 10;

        /// <summary><c>statuscode</c> value for a failed async job.</summary>
        public const int StatusCodeFailed = 31;

        /// <summary><c>statuscode</c> value for a cancelled async job.</summary>
        public const int StatusCodeCanceled = 32;
    }

    /// <summary>
    /// Standard table <c>plugintracelog</c> — plug-in / custom-workflow-activity
    /// execution traces. Populated only when plug-in trace logging is enabled in
    /// the environment (System Settings → Customization → plug-in trace log).
    /// </summary>
    public static class PluginTraceLog
    {
        public const string EntityName = "plugintracelog";
        public const string PluginTraceLogId = "plugintracelogid";
        public const string CreatedOn = "createdon";
        public const string TypeName = "typename";
        public const string MessageName = "messagename";
        public const string PrimaryEntity = "primaryentity";
        public const string Mode = "mode";
        public const string Depth = "depth";
        public const string OperationType = "operationtype";
        public const string ExceptionDetails = "exceptiondetails";
        public const string MessageBlock = "messageblock";
        public const string CorrelationId = "correlationid";
        public const string PerformanceExecutionDuration = "performanceexecutionduration";
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
