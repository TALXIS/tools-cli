using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Core.Contracts;
using Microsoft.TemplateEngine.Edge.Template;
using TALXIS.CLI.Logging;


namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    public class PostActionDispatcher
    {
        private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(PostActionDispatcher));
        private readonly Dictionary<Guid, IPostActionProcessor> _processors;
        private readonly IEngineEnvironmentSettings _environment;
        private readonly Func<ScriptPermission, IPostAction, bool> _allowScripts;

        public PostActionDispatcher(IEngineEnvironmentSettings environment, Func<ScriptPermission, IPostAction, bool> allowScripts)
        {
            _environment = environment;
            _allowScripts = allowScripts;
            _processors = new Dictionary<Guid, IPostActionProcessor>
            {
                { RunScriptPostActionProcessor.ActionProcessorId, new RunScriptPostActionProcessor() },
                { AddReferencePostActionProcessor.ActionProcessorId, new AddReferencePostActionProcessor() },
                { AddProjectsToSlnPostActionProcessor.ActionProcessorId, new AddProjectsToSlnPostActionProcessor() },
                { AddRootComponentToSolutionXmlProcessor.ActionProcessorId, new AddRootComponentToSolutionXmlProcessor() },
                { SortXmlElementsProcessor.ActionProcessorId, new SortXmlElementsProcessor() },
                { ReplaceOptionValuePrefixProcessor.ActionProcessorId, new ReplaceOptionValuePrefixProcessor() }
            };
        }

        /// <summary>
        /// Per-action error details from the last RunPostActions call, keyed by the action's ActionId.
        /// </summary>
        public Dictionary<Guid, string> FailedActionErrors { get; } = new();

        public (PostActionResult, List<IPostAction>) RunPostActions(
            IReadOnlyList<IPostAction> actions, 
            ScriptPermission scriptPermission,
            ITemplateCreationResult? templateCreationResult = null,
            string? outputBasePath = null,
            PostActionTransaction? transaction = null)
        {
            FailedActionErrors.Clear();
            var result = PostActionResult.Success;
            var failedActions = new List<IPostAction>();

            foreach (var action in actions)
            {
                var actionLabel = !string.IsNullOrWhiteSpace(action.Description)
                    ? action.Description
                    : action.ActionId.ToString();

                if (!_processors.TryGetValue(action.ActionId, out var processor))
                {
                    _logger.LogError("Post-action '{ActionLabel}' is not supported. Please run manually:", actionLabel);
                    ShowManualInstructions(action);
                    result |= PostActionResult.Failure;
                    failedActions.Add(action);
                    continue;
                }

                if (processor is RunScriptPostActionProcessor && !_allowScripts(scriptPermission, action))
                {
                    _logger.LogInformation("Skipping script post-action '{ActionLabel}' due to script policy", actionLabel);
                    continue;
                }

                // Use ProcessInternal if available and we have templateCreationResult, otherwise fall back to Process
                bool ok;
                if (processor is AddProjectsToSlnPostActionProcessor addProjectProcessor && templateCreationResult?.CreationResult != null)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    ok = addProjectProcessor.ProcessInternal(_environment, action, null!, templateCreationResult!.CreationResult, basePath);
                }
                else if (processor is AddReferencePostActionProcessor addReferenceProcessor)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    ok = addReferenceProcessor.ProcessInternal(_environment, action, basePath);
                }
                else if (processor is RunScriptPostActionProcessor runScriptProcessor)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    ok = runScriptProcessor.ProcessInternal(_environment, action, null!, templateCreationResult?.CreationResult, basePath);
                    if (!ok && runScriptProcessor.LastError != null)
                    {
                        FailedActionErrors[action.ActionId] = runScriptProcessor.LastError;
                    }
                }
                else if (processor is AddRootComponentToSolutionXmlProcessor addRootComponentProcessor)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    if (transaction != null)
                    {
                        // Track the Solution.xml file for rollback before modification
                        var solutionXmlPath = FindSolutionXml(basePath);
                        if (solutionXmlPath != null)
                        {
                            transaction.TrackFile(solutionXmlPath);
                        }
                    }
                    ok = addRootComponentProcessor.ProcessInternal(_environment, action, null!, templateCreationResult?.CreationResult, basePath);
                }
                else if (processor is SortXmlElementsProcessor sortXmlProcessor)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    ok = sortXmlProcessor.ProcessInternal(_environment, action, null!, templateCreationResult?.CreationResult, basePath);
                }
                else if (processor is ReplaceOptionValuePrefixProcessor replaceOptionValuePrefixProcessor)
                {
                    var basePath = outputBasePath ?? Directory.GetCurrentDirectory();
                    if (transaction != null)
                    {
                        // Track the Solution.xml file for rollback before modification
                        var solutionXmlPath = FindSolutionXml(basePath);
                        if (solutionXmlPath != null)
                        {
                            transaction.TrackFile(solutionXmlPath);
                        }
                    }
                    ok = replaceOptionValuePrefixProcessor.ProcessInternal(_environment, action, null!, templateCreationResult?.CreationResult, basePath);
                }
                else
                {
                    ok = processor.Process(_environment, action);
                }
                
                if (!ok)
                {
                    result |= PostActionResult.Failure;
                    failedActions.Add(action);
                    var errorDetail = FailedActionErrors.TryGetValue(action.ActionId, out var detail) ? $": {detail}" : "";
                    _logger.LogError("Post-action '{ActionLabel}' failed{ErrorDetail}", actionLabel, errorDetail);
                    if (!action.ContinueOnError)
                    {
                        _logger.LogError("Stopping post-action execution (continueOnError is false)");
                        transaction?.Rollback();
                        break;
                    }
                }
            }
            if (!failedActions.Any())
            {
                transaction?.Commit();
            }

            return (result, failedActions);
        }

        private void ShowManualInstructions(IPostAction action)
        {
            _logger.LogInformation("{Instructions}", action.ManualInstructions ?? "No manual instructions provided.");
        }

        /// <summary>
        /// Locates Solution.xml by walking up from the given path, looking for Other/Solution.xml.
        /// </summary>
        private static string? FindSolutionXml(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Other", "Solution.xml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
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
        No = 0,
        Yes = 1,
        Prompt = 2
    }
}
