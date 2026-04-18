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
        /// <param name="descriptor">The tool descriptor to register.</param>
        public void AddDescriptor(McpToolDescriptor descriptor)
        {
            _descriptors[descriptor.Name] = descriptor;
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
