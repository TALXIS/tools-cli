using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Core.Contracts;
using Microsoft.TemplateEngine.Edge.Template;


namespace TALXIS.CLI.Workspace.TemplateEngine
{
    public class PostActionDispatcher
    {
        private readonly Dictionary<Guid, IPostActionProcessor> _processors;
        private readonly IEngineEnvironmentSettings _environment;
        private readonly Func<ScriptPermission, IPostAction, bool> _allowScripts;

        public PostActionDispatcher(IEngineEnvironmentSettings environment, Func<ScriptPermission, IPostAction, bool> allowScripts)
        {
            _environment = environment;
            _allowScripts = allowScripts;
            _processors = new Dictionary<Guid, IPostActionProcessor>
            {
                { new Guid("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2"), new RunScriptPostActionProcessor() },
                { new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814"), new AddReferencePostActionProcessor() },
                { new Guid("D396686C-DE0E-4DE6-906D-291CD29FC5DE"), new AddProjectsToSlnPostActionProcessor() }
            };
        }

        public (PostActionResult, List<IPostAction>) RunPostActions(
            IReadOnlyList<IPostAction> actions, 
            ScriptPermission scriptPermission,
            ITemplateCreationResult? templateCreationResult = null,
            string? outputBasePath = null)
        {
            var result = PostActionResult.Success;
            var failedActions = new List<IPostAction>();
            foreach (var action in actions)
            {
                if (!_processors.TryGetValue(action.ActionId, out var processor))
                {
                    Console.Error.WriteLine($"Post-action {action.ActionId} not supported. Please run manually:");
                    ShowManualInstructions(action);
                    result |= PostActionResult.Failure;
                    failedActions.Add(action);
                    continue;
                }

                if (processor is RunScriptPostActionProcessor && !_allowScripts(scriptPermission, action))
                {
                    Console.WriteLine($"Skipping script post-action {action.Description} due to script policy.");
                    continue;
                }

                // Use ProcessInternal if available and we have templateCreationResult, otherwise fall back to Process
                bool ok;
                if (processor is AddProjectsToSlnPostActionProcessor addProjectProcessor && templateCreationResult?.CreationResult != null)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    // We already checked for null above, so we can safely access the property
                    ok = addProjectProcessor.ProcessInternal(_environment, action, null!, templateCreationResult!.CreationResult, basePath);
                }
                else if (processor is RunScriptPostActionProcessor runScriptProcessor)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    // Use ProcessInternal with explicit outputBasePath for consistent working directory handling
                    ok = runScriptProcessor.ProcessInternal(_environment, action, null!, templateCreationResult?.CreationResult, basePath);
                }
                else
                {
                    ok = processor.Process(_environment, action);
                }
                
                if (!ok)
                {
                    result |= PostActionResult.Failure;
                    failedActions.Add(action);
                    if (!action.ContinueOnError)
                        throw new InvalidOperationException($"Post-action {action.ActionId} failed");
                }
            }
            return (result, failedActions);
        }

        private void ShowManualInstructions(IPostAction action)
        {
            Console.WriteLine(action.ManualInstructions ?? "No manual instructions provided.");
        }
    }

    [Flags]
    public enum PostActionResult
    {
        None = 0,
        Success = 1,
        Failure = 2,
        Cancelled = 4
    }

    public enum ScriptPermission
    {
        No,
        Yes,
        Prompt
    }
}
