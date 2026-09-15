namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline;

internal interface ISolutionPullStep
{
    void Execute(SolutionPullContext context);
}
