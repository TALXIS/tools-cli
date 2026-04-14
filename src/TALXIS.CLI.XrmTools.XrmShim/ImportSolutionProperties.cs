namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// String constants used by CrmPackageCore when building ImportSolution requests.
/// These must match the names expected by the Dataverse ImportSolution message.
/// </summary>
public static class ImportSolutionProperties
{
    public static string DESIREDLAYERORDERPARAM = "DesiredLayerOrder";
    public static string ASYNCRIBBONPROCESSING = "AsyncRibbonProcessing";
    public static string SOLUTIONNAMEPARAM = "SolutionName";
    public static string COMPONENTPARAMETERSPARAM = "ComponentParameters";
    public static string CONVERTTOMANAGED = "ConvertToManaged";
    public static string TEMPLATESUFFIX = "TemplateSuffix";
    public static string ISTEMPLATEMODE = "IsTemplateMode";
    public static string USESTAGEANDUPGRADEMODE = "StageAndUpgradeSolution";
    public static string SCHEMAUPDATESONLY = "SchemaUpdatesOnly";
    public static string SOLUTIONCOMPONENTOPTIONS = "SolutionComponentOptions";
}
