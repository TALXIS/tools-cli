using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    /// <summary>
    /// Service responsible for validating template parameters.
    /// </summary>
    public class TemplateParameterValidator
    {
        private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(TemplateParameterValidator));
        public void ValidateParameters(ITemplateInfo template, IDictionary<string, string> userParameters)
        {
            var errors = new List<string>();
            
            // Get only user-input parameters (exclude system parameters like name, type, language)
            var templateParameters = template.ParameterDefinitions
                .Where(p => p.Name != "type" && p.Name != "language" && p.Name != "name")
                .ToList();

            // Reject parameters the template doesn't define. Silently ignoring an
            // unknown --param is the most dangerous failure mode: the command "succeeds"
            // but the value has no effect (e.g. a hallucinated 'FormatName').
            var knownNames = new HashSet<string>(
                template.ParameterDefinitions.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var suggestionCandidates = templateParameters.Select(p => p.Name).ToList();
            var templateName = template.ShortNameList?.FirstOrDefault() ?? template.Name;
            errors.AddRange(FindUnknownParameters(userParameters.Keys, knownNames, suggestionCandidates, templateName));

            foreach (var templateParam in templateParameters)
            {
                var paramName = templateParam.Name;
                var hasUserValue = userParameters.ContainsKey(paramName);
                var userValue = hasUserValue ? userParameters[paramName] : null;

                // Check required parameters
                var isRequired = templateParam.Precedence?.ToString() == "Required";
                if (isRequired && string.IsNullOrWhiteSpace(userValue))
                {
                    errors.Add($"Parameter '{paramName}' is required but was not provided.");
                    continue; // Skip further validation for missing required parameters
                }

                // Skip validation if no value provided and parameter is optional
                if (!hasUserValue || string.IsNullOrWhiteSpace(userValue))
                {
                    continue;
                }

                // Validate parameter value with detailed error messages
                ValidateParameterValue(templateParam, userValue, errors);
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException($"Parameter validation failed:\n{string.Join("\n", errors)}");
            }
        }

        private static void ValidateParameterValue(ITemplateParameter templateParam, string userValue, List<string> errors)
        {
            var paramName = templateParam.Name;

            // Validate based on data type
            switch (templateParam.DataType?.ToLowerInvariant())
            {
                case "bool":
                case "boolean":
                    if (!bool.TryParse(userValue, out _))
                    {
                        errors.Add($"Parameter '{paramName}' expects a boolean value (true/false), but got '{userValue}'.");
                    }
                    break;

                case "int":
                case "integer":
                    if (!int.TryParse(userValue, out _))
                    {
                        errors.Add($"Parameter '{paramName}' expects an integer value, but got '{userValue}'.");
                    }
                    break;

                case "choice":
                    // Validate against allowed choices with detailed error message
                    if (templateParam.Choices != null && templateParam.Choices.Count > 0)
                    {
                        var validChoices = templateParam.Choices.Keys.ToList();

                        if (!validChoices.Contains(userValue, StringComparer.OrdinalIgnoreCase))
                        {
                            var choicesStr = string.Join(", ", validChoices.Select(c => $"'{c}'"));
                            errors.Add($"Parameter '{paramName}' must be one of: {choicesStr}. Got '{userValue}'.");
                        }
                    }
                    break;

                case "text":
                case "string":
                    // Text parameters generally don't need additional validation beyond required check
                    // Could add length validation here if needed
                    break;

                default:
                    // For unknown data types, just log a warning but don't fail validation
                    _logger.LogWarning("Unknown data type {DataType} for parameter {ParamName}. Skipping validation.", templateParam.DataType, paramName);
                    break;
            }
        }

        /// <summary>
        /// Returns an error for every user-supplied parameter key the template does not
        /// define, with a best-effort "did you mean" suggestion (closest known name).
        /// </summary>
        public static IReadOnlyList<string> FindUnknownParameters(
            IEnumerable<string> userKeys,
            ISet<string> knownNames,
            IReadOnlyList<string> suggestionCandidates,
            string templateName)
        {
            var errors = new List<string>();
            foreach (var key in userKeys)
            {
                if (knownNames.Contains(key))
                {
                    continue;
                }

                var suggestion = FindClosest(key, suggestionCandidates);
                var hint = suggestion is null ? string.Empty : $" Did you mean '{suggestion}'?";
                errors.Add($"Unknown parameter '{key}' for template '{templateName}'.{hint}");
            }
            return errors;
        }

        /// <summary>
        /// Finds the candidate closest to <paramref name="input"/> by Levenshtein distance,
        /// within a small typo-sized threshold. Returns <c>null</c> when nothing is close.
        /// </summary>
        private static string? FindClosest(string input, IReadOnlyList<string> candidates)
        {
            string? best = null;
            int bestDistance = int.MaxValue;
            int threshold = Math.Max(2, input.Length / 3);

            foreach (var candidate in candidates)
            {
                var distance = Levenshtein(input.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return bestDistance <= threshold ? best : null;
        }

        private static int Levenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }
    }
}
