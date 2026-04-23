namespace TALXIS.CLI.Config.Providers.Dataverse.Platforms;

/// <summary>
/// Logical names of Dataverse entities used by the deployment domain
/// (Deployment Controller solution history and Package Deployer run history).
/// </summary>
public static class DeploymentSchema
{
    public static class SolutionHistory
    {
        public const string EntityName = "msdyn_solutionhistory";
    }

    public static class PackageHistory
    {
        public const string EntityName = "packagehistory";
    }
}
