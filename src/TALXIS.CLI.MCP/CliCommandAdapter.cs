using System.Reflection;
using System.Text.Json;

namespace TALXIS.CLI.MCP
{
    public class CliCommandAdapter
    {
        public List<string> BuildCliArgs(string toolName, IReadOnlyDictionary<string, JsonElement>? arguments)
        {
            // Split tool name into CLI command parts
            var cliArgs = ParseToolName(toolName);
            var commandType = new McpToolRegistry().FindCommandTypeByToolName(toolName);
            var positionalArgs = new List<string>();
            var optionArgs = new List<string>();

            if (arguments != null && commandType != null)
            {
                // Handle CliArgument (positional) in order
                var positionalProps = GetPositionalProperties(commandType);
                foreach (var prop in positionalProps)
                {
                    var name = prop.Attr?.Name ?? prop.Prop.Name;
                    if (arguments.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null)
                    {
                        AddArgumentValues(positionalArgs, value, null);
                    }
                }

                // Handle CliOption (named)
                var optionProps = GetOptionProperties(commandType);
                foreach (var entry in arguments)
                {
                    // Only add as option if not already used as positional
                    if (positionalProps.Any(x => (x.Attr?.Name ?? x.Prop.Name) == entry.Key))
                        continue;
                    if (entry.Value.ValueKind != JsonValueKind.Null)
                    {
                        // Find the property info for this option to get the correct name/casing
                        var optionProp = optionProps.FirstOrDefault(x => (x.Attr?.Name ?? x.Prop.Name) == entry.Key);
                        var optionName = optionProp.Attr?.Name ?? optionProp.Prop?.Name ?? entry.Key;
                        // DotMake uses lower camelCase if no attribute name is set
                        if (optionProp.Attr?.Name == null && optionProp.Prop != null)
                        {
                            optionName = char.ToLowerInvariant(optionProp.Prop.Name[0]) + optionProp.Prop.Name.Substring(1);
                        }
                        AddArgumentValues(optionArgs, entry.Value, $"--{optionName}=");
                    }
                }
            }
            else if (arguments != null)
            {
                // Fallback: treat all as options
                foreach (var entry in arguments)
                {
                    if (entry.Value.ValueKind != JsonValueKind.Null)
                    {
                        AddArgumentValues(optionArgs, entry.Value, $"--{entry.Key}=");
                    }
                }
            }

            cliArgs.AddRange(positionalArgs);
            cliArgs.AddRange(optionArgs);
            return cliArgs;
        }

        // Helper: Split tool name by underscores
        private static List<string> ParseToolName(string toolName)
        {
            return toolName.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // Helper: Get positional (CliArgument) properties
        private static List<(PropertyInfo Prop, DotMake.CommandLine.CliArgumentAttribute? Attr)> GetPositionalProperties(Type commandType)
        {
            return commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Prop: p, Attr: p.GetCustomAttribute(typeof(DotMake.CommandLine.CliArgumentAttribute)) as DotMake.CommandLine.CliArgumentAttribute))
                .Where(x => x.Attr != null)
                .ToList();
        }

        // Helper: Get option (CliOption) properties
        private static List<(PropertyInfo Prop, DotMake.CommandLine.CliOptionAttribute? Attr)> GetOptionProperties(Type commandType)
        {
            return commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Prop: p, Attr: p.GetCustomAttribute(typeof(DotMake.CommandLine.CliOptionAttribute)) as DotMake.CommandLine.CliOptionAttribute))
                .Where(x => x.Attr != null || x.Prop != null)
                .ToList();
        }

        // Helper: Add argument values to target list, handling arrays and single values
        // For array options (e.g., List<string>), emit repeated --Option value pairs (not --Option=value)
        // For single values, emit --Option value or --Option=value as appropriate
        private static void AddArgumentValues(List<string> target, JsonElement value, string? prefix)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    AddSingleArgumentValue(target, item, prefix);
                }
            }
            else
            {
                AddSingleArgumentValue(target, value, prefix);
            }
        }

        // Adds a single argument value to the target list, handling prefix logic
        private static void AddSingleArgumentValue(List<string> target, JsonElement value, string? prefix)
        {
            if (prefix != null && prefix.EndsWith("="))
            {
                // e.g., --Param value
                var opt = prefix.TrimEnd('=');
                target.Add(opt);
                target.Add(value.ToString());
            }
            else if (prefix != null)
            {
                // e.g., --flagTrue
                target.Add(prefix + value);
            }
            else
            {
                // Just the value
                target.Add(value.ToString());
            }
        }

        public JsonElement BuildInputSchema(Type commandType)
        {
            var properties = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var required = new List<string>();
            var schemaProperties = new Dictionary<string, object?>();

            // Add CliArgument (positional) properties
            foreach (var prop in properties)
            {
                var argAttr = prop.GetCustomAttribute(typeof(DotMake.CommandLine.CliArgumentAttribute)) as DotMake.CommandLine.CliArgumentAttribute;
                if (argAttr != null)
                {
                    var type = GetJsonSchemaType(prop.PropertyType, out var itemsSchema);
                    var argName = argAttr.Name ?? prop.Name;
                    var schemaProp = new Dictionary<string, object?>
                    {
                        ["type"] = type,
                        ["description"] = argAttr.Description
                    };
                    if (itemsSchema != null)
                        schemaProp["items"] = itemsSchema;
                    schemaProperties[argName] = schemaProp;
                    required.Add(argName);
                }
            }

            // Add CliOption (named) properties
            foreach (var prop in properties)
            {
                var optionAttr = prop.GetCustomAttribute(typeof(DotMake.CommandLine.CliOptionAttribute)) as DotMake.CommandLine.CliOptionAttribute;
                if (optionAttr != null)
                {
                    var type = GetJsonSchemaType(prop.PropertyType, out var itemsSchema);
                    var optionName = (optionAttr.Name ?? prop.Name).TrimStart('-');
                    var schemaProp = new Dictionary<string, object?>
                    {
                        ["type"] = type,
                        ["description"] = optionAttr.Description
                    };
                    if (itemsSchema != null)
                        schemaProp["items"] = itemsSchema;
                    schemaProperties[optionName] = schemaProp;
                    if (optionAttr.Required)
                        required.Add(optionName);
                }
            }

            var schema = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = schemaProperties,
                ["required"] = required
            };
            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
        }

        // Helper: Get JSON schema type for a .NET type
        private static string GetJsonSchemaType(Type type, out object? itemsSchema)
        {
            itemsSchema = null;
            if (type == typeof(bool))
                return "boolean";
            if (type == typeof(string))
                return "string";
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            {
                // Try to get the element type
                var elementType = type.IsArray ? type.GetElementType() :
                    (type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null);
                itemsSchema = new Dictionary<string, object?> { ["type"] = elementType == typeof(bool) ? "boolean" : "string" };
                return "array";
            }
            return "string";
        }
    }
}
