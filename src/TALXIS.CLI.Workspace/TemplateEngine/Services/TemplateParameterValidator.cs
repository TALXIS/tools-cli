using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Service responsible for validating template parameters.
    /// </summary>
    public class TemplateParameterValidator : ITemplateParameterValidator
    {
        public void ValidateParameters(ITemplateInfo template, IDictionary<string, string> userParameters)
        {
            var errors = new List<string>();
            
            // Get only user-input parameters (exclude system parameters like name, type, language)
            var templateParameters = template.ParameterDefinitions
                .Where(p => p.Name != "type" && p.Name != "language" && p.Name != "name")
                .ToList();

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
                    Console.WriteLine($"Warning: Unknown data type '{templateParam.DataType}' for parameter '{paramName}'. Skipping validation.");
                    break;
            }
        }
    }
}
