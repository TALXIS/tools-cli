using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Result of template scaffolding operation.
    /// </summary>
    public class TemplateScaffoldResult
    {
        public bool Success { get; set; }
        public List<IPostAction> FailedActions { get; set; } = new();
    }


}
