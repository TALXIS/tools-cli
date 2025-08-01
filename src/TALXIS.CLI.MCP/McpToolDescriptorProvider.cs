using System;
using System.Collections.Generic;

namespace TALXIS.CLI.MCP
{
    /// <summary>
    /// Provides registration and lookup for MCP tool descriptors, enabling mapping between tool names and their descriptors.
    /// </summary>
    public class McpToolDescriptorProvider
    {
        private readonly Dictionary<string, McpToolDescriptor> _descriptors = new();

        /// <summary>
        /// Registers a descriptor for a MCP tool.
        /// </summary>
        /// <param name="name">The unique name of the tool.</param>
        /// <param name="description">A description of the tool.</param>
        /// <param name="toolType">The <see cref="Type"/> implementing the tool.</param>
        public void AddDescriptor(string name, string description, Type toolType)
        {
            _descriptors[name] = new McpToolDescriptor { Name = name, Description = description, CliCommandClass = toolType };
        }

        /// <summary>
        /// Gets the descriptor for a MCP tool by name.
        /// </summary>
        /// <param name="name">The name of the tool.</param>
        /// <returns>The <see cref="McpToolDescriptor"/> if found; otherwise, null.</returns>
        public McpToolDescriptor? GetDescriptor(string name)
        {
            _descriptors.TryGetValue(name, out var descriptor);
            return descriptor;
        }

        /// <summary>
        /// Gets descriptors for all registered MCP tools.
        /// </summary>
        /// <returns>An enumerable of <see cref="McpToolDescriptor"/>.</returns>
        public IEnumerable<McpToolDescriptor> GetAllDescriptors()
        {
            return _descriptors.Values;
        }
    }
}
