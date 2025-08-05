using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.MCP.Tests
{
    public class McpServerIntegrationTests
    {
        private static readonly string ProjectPath = GetProjectPath();

        private static string GetProjectPath()
        {
            // Start from test output dir, traverse up to repo root
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TALXIS.CLI.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
                throw new DirectoryNotFoundException("Could not find repo root (TALXIS.CLI.sln)");
            var projectPath = Path.Combine(dir.FullName, "src", "TALXIS.CLI.MCP", "TALXIS.CLI.MCP.csproj");
            Console.WriteLine($"Resolved MCP project path: {projectPath}");
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"MCP project file not found at: {projectPath}");
            return projectPath;
        }

        static McpServerIntegrationTests()
        {
            Console.WriteLine($"Resolved MCP project path: {ProjectPath}");
            if (!File.Exists(ProjectPath))
            {
                throw new FileNotFoundException($"MCP project file not found at: {ProjectPath}");
            }
        }


        [Fact]
        public async Task Initialize_And_ListTools_Works()
        {
            await WithMcpServer(async process =>
            {
                await InitializeMcp(process);
                var response = await ReadResponse(process);
                Assert.Contains("result", response);

                await SendJsonRpc(process, new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
                response = await ReadResponse(process);
                Assert.Contains("result", response);
                Assert.Contains("workspace_component_create", response);
            });
        }


        [Fact]
        public async Task InvalidToolName_ReturnsError()
        {
            await WithMcpServer(async process =>
            {
                await InitializeMcp(process);
                await ReadResponse(process);

                await SendJsonRpc(process, new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "nonexistent_tool", arguments = new { } } });
                var response = await ReadResponse(process);
                Assert.Contains("error", response, StringComparison.OrdinalIgnoreCase);
            });
        }


        [Fact]
        public async Task WorkspaceComponentCreate_MissingRequiredParam_ReturnsError()
        {
            await WithMcpServer(async process =>
            {
                await InitializeMcp(process);
                await ReadResponse(process);

                // Missing required parameters (e.g. name, template)
                await SendJsonRpc(process, new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "workspace_component_create", arguments = new { } } });
                var response = await ReadResponse(process);
                Assert.Contains("error", response, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("required", response, StringComparison.OrdinalIgnoreCase);
            });
        }


        [Fact]
        public async Task WorkspaceComponentCreate_InvalidTemplate_ReturnsError()
        {
            await WithMcpServer(async process =>
            {
                await InitializeMcp(process);
                await ReadResponse(process);

                // Invalid template name
                await SendJsonRpc(process, new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "workspace_component_create", arguments = new { name = "TestComponent", template = "invalid-template" } } });
                var response = await ReadResponse(process);
                Assert.Contains("error", response, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("template", response, StringComparison.OrdinalIgnoreCase);
            });
        }
        /// <summary>
        /// Helper to start the MCP server, run a test, and ensure cleanup.
        /// </summary>
        private static async Task WithMcpServer(Func<Process, Task> test)
        {
            var process = StartMcpServer();
            try
            {
                await test(process);
            }
            finally
            {
                process.Kill();
            }
        }

        /// <summary>
        /// Helper to send the MCP initialize message.
        /// </summary>
        private static Task InitializeMcp(Process process)
        {
            return SendJsonRpc(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "test-client", version = "1.0.0" }
                }
            });
        }

        private static Process StartMcpServer()
        {
            var psi = new ProcessStartInfo("dotnet", $"run --project {ProjectPath}")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            return Process.Start(psi);
        }

        private static async Task SendJsonRpc(Process process, object message)
        {
            var json = JsonSerializer.Serialize(message);
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
        }

        private static async Task<string> ReadResponse(Process process)
        {
            // Read a single line response
            return await process.StandardOutput.ReadLineAsync();
        }
    }
}
