using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    /// <summary>
    /// Post-action processor that sorts child XML elements of a matched node by a specified attribute.
    /// Used to maintain deterministic ordering of elements like entity attributes in Entity.xml.
    /// </summary>
    public class SortXmlElementsProcessor : IPostActionProcessor
    {
        private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SortXmlElementsProcessor));
        public static Guid ActionProcessorId => new Guid("A1B2C3D4-1002-4000-8000-000000000002");
        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            return ProcessInternal(environment, action, null!, null!, System.Environment.CurrentDirectory);
        }

        public bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, ICreationEffects creationEffects, ICreationResult? templateCreationResult, string outputBasePath)
        {
            var args = action.Args;

            if (!args.TryGetValue("filePattern", out var filePattern))
            {
                _logger.LogError("[SortXmlElements] Missing required 'filePattern' argument");
                return false;
            }

            if (!args.TryGetValue("xpath", out var xpath))
            {
                _logger.LogError("[SortXmlElements] Missing required 'xpath' argument");
                return false;
            }

            if (!args.TryGetValue("sortBy", out var sortBy))
            {
                _logger.LogError("[SortXmlElements] Missing required 'sortBy' argument");
                return false;
            }

            var caseSensitive = args.TryGetValue("caseSensitive", out var cs) &&
                                cs.Equals("true", StringComparison.OrdinalIgnoreCase);

            try
            {
                var matchingFiles = FindMatchingFiles(outputBasePath, filePattern);
                if (matchingFiles.Count == 0)
                {
                    _logger.LogInformation("[SortXmlElements] No files matching '{Pattern}' found in '{Path}' — skipping", filePattern, outputBasePath);
                    return true;
                }

                foreach (var filePath in matchingFiles)
                {
                    _logger.LogInformation("[SortXmlElements] Sorting elements in {Path} (xpath={XPath}, sortBy={SortBy})", filePath, xpath, sortBy);
                    SortElementsInFile(filePath, xpath, sortBy, caseSensitive);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[SortXmlElements] Failed: {Message}", ex.Message);
                return false;
            }
        }

        private void SortElementsInFile(string filePath, string xpath, string sortBy, bool caseSensitive)
        {
            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.Load(filePath);

            var parentNodes = doc.SelectNodes(xpath);
            if (parentNodes == null || parentNodes.Count == 0)
            {
                _logger.LogInformation("[SortXmlElements] No nodes matching xpath '{XPath}' in {Path}", xpath, filePath);
                return;
            }

            foreach (XmlNode parentNode in parentNodes)
            {
                var children = new List<XmlNode>();
                foreach (XmlNode child in parentNode.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element)
                    {
                        children.Add(child);
                    }
                }

                if (children.Count == 0) continue;

                var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
                var sorted = children
                    .OrderBy(node => (node as XmlElement)?.GetAttribute(sortBy) ?? "", comparer)
                    .ToList();

                // Remove all element children
                foreach (var child in children)
                {
                    parentNode.RemoveChild(child);
                }

                // Re-add in sorted order
                foreach (var child in sorted)
                {
                    parentNode.AppendChild(child);
                }
            }

            // Save with settings matching the original PowerShell script behavior
            var settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineHandling = NewLineHandling.None,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(true) // UTF-8 with BOM
            };

            using var writer = XmlWriter.Create(filePath, settings);
            doc.Save(writer);
        }

        /// <summary>
        /// Finds files matching a glob-like pattern relative to a base path.
        /// Supports simple patterns like "Entities/*/Entity.xml".
        /// </summary>
        private static List<string> FindMatchingFiles(string basePath, string pattern)
        {
            var results = new List<string>();

            // Split the pattern into directory parts
            var parts = pattern.Replace('\\', '/').Split('/');
            SearchRecursive(basePath, parts, 0, results);

            return results;
        }

        private static void SearchRecursive(string currentDir, string[] parts, int partIndex, List<string> results)
        {
            if (!Directory.Exists(currentDir)) return;

            if (partIndex >= parts.Length) return;

            var part = parts[partIndex];
            var isLastPart = partIndex == parts.Length - 1;

            if (part == "*" || part == "**")
            {
                // Wildcard: match all subdirectories at this level
                foreach (var dir in Directory.GetDirectories(currentDir))
                {
                    if (isLastPart)
                    {
                        // * as last part doesn't match files, only if the pattern has more
                        continue;
                    }
                    SearchRecursive(dir, parts, partIndex + 1, results);
                }

                // For **, also try matching deeper
                if (part == "**")
                {
                    foreach (var dir in Directory.GetDirectories(currentDir))
                    {
                        SearchRecursive(dir, parts, partIndex, results);
                    }
                    // Also try matching the next part in the current directory
                    SearchRecursive(currentDir, parts, partIndex + 1, results);
                }
            }
            else if (isLastPart)
            {
                // Last part: match files
                var filePath = Path.Combine(currentDir, part);
                if (File.Exists(filePath))
                {
                    results.Add(filePath);
                }
            }
            else
            {
                // Exact directory name: descend into it
                var nextDir = Path.Combine(currentDir, part);
                if (Directory.Exists(nextDir))
                {
                    SearchRecursive(nextDir, parts, partIndex + 1, results);
                }
            }
        }
    }
}
