

using System;
using System.Collections.Generic;
using System.Reflection;

namespace TALXIS.CLI.MCP
{
    public class CliCommandLookupService
    {
        public IEnumerable<McpToolDescriptor> EnumerateAllCommands(Type rootType)
        {
            var results = new List<McpToolDescriptor>();
            EnumerateRecursive(rootType, new List<string>(), rootType, results);
            return results;
        }

        private void EnumerateRecursive(Type cmdType, List<string> parentSegments, Type rootType, List<McpToolDescriptor> results)
        {
            var attr = Attribute.GetCustomAttribute(cmdType, typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
            if (attr == null) return;
            var cliCommandNameResolver = new CliCommandNameResolver();
            string name = cliCommandNameResolver.ResolveCommandName(cmdType, attr);
            bool isRoot = cmdType == rootType;
            bool isDirectChildOfRoot = (parentSegments.Count == 0) && isRoot == false;
            bool isGroup = attr.Children != null && attr.Children.Length > 0;
            var segments = isRoot ? new List<string>() : new List<string>(parentSegments) { name };
            var fullName = string.Join("_", segments);
            if (!isGroup && !isRoot && !isDirectChildOfRoot)
            {
                results.Add(new McpToolDescriptor
                {
                    Name = fullName,
                    Description = attr.Description,
                    CliCommandClass = cmdType
                });
            }
            if (attr.Children != null)
            {
                foreach (var child in attr.Children)
                    EnumerateRecursive(child, segments, rootType, results);
            }
        }
        public Type? FindCommandTypeByToolName(string toolName, Type rootType)
        {
            var segments = toolName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return FindCommandTypeBySegments(segments, 0, rootType, skipRoot: true);
        }

        private Type? FindCommandTypeBySegments(string[] segments, int index, Type type, bool skipRoot = false)
        {
            var attr = Attribute.GetCustomAttribute(type, typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
            if (attr == null) return null;
            var cliCommandNameResolver = new CliCommandNameResolver();
            string cmdName = cliCommandNameResolver.ResolveCommandName(type, attr);
            if (skipRoot)
            {
                if (attr.Children != null)
                {
                    foreach (var child in attr.Children)
                    {
                        var found = FindCommandTypeBySegments(segments, index, child, skipRoot: false);
                        if (found != null) return found;
                    }
                }
                return null;
            }
            if (!string.Equals(cmdName, segments[index], StringComparison.OrdinalIgnoreCase))
                return null;
            if (index == segments.Length - 1)
                return type;
            if (attr.Children != null)
            {
                foreach (var child in attr.Children)
                {
                    var found = FindCommandTypeBySegments(segments, index + 1, child, skipRoot: false);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
