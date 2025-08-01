using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace
{
    // Minimal local interface for post action processors
    public interface IPostActionProcessor
    {
        Guid ActionId { get; }
        bool Process(IEngineEnvironmentSettings environment, IPostAction action);
    }
}
