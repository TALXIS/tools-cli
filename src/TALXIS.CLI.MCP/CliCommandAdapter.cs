using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace TALXIS.CLI.MCP
{
    public class CliCommandAdapter
    {
        public List<string> BuildCliArgs(string toolName, IReadOnlyDictionary<string, JsonElement>? arguments)
        {
            var cliArgs = toolName.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (arguments != null)
            {
                foreach (var entry in arguments)
                {
                    if (entry.Value.ValueKind != JsonValueKind.Null)
                        cliArgs.Add($"--{entry.Key}={entry.Value}");
                }
            }
            return cliArgs;
        }

        public JsonElement BuildInputSchema(Type commandType)
        {
            var properties = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var required = new List<string>();
            var schemaProperties = new Dictionary<string, object?>();

            foreach (var prop in properties)
            {
                var optionAttr = prop.GetCustomAttribute(typeof(DotMake.CommandLine.CliOptionAttribute)) as DotMake.CommandLine.CliOptionAttribute;
                if (optionAttr == null) continue;
                var type = prop.PropertyType == typeof(bool) ? "boolean" : "string";
                var optionName = (optionAttr.Name ?? prop.Name).TrimStart('-');
                schemaProperties[optionName] = new Dictionary<string, object?>
                {
                    ["type"] = type,
                    ["description"] = optionAttr.Description
                };
                if (optionAttr.Required)
                    required.Add(optionName);
            }

            var schema = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = schemaProperties,
                ["required"] = required
            };
            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
        }
    }
}
