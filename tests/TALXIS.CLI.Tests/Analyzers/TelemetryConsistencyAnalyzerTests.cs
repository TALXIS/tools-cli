using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TALXIS.CLI.Analyzers;
using Xunit;

namespace TALXIS.CLI.Tests.Analyzers;

public class TelemetryConsistencyAnalyzerTests
{
    [Fact]
    public async Task NoDirectActivityTagging_FlagsCommandActivitySetTag()
    {
        const string source = """
            using System.Diagnostics;
            using DotMake.CommandLine;
            using TALXIS.CLI.Core.Shared;

            [CliCommand]
            public sealed class BadCommand : TxcLeafCommand
            {
                public void Execute()
                {
                    var activity = Activity.Current;
                    if (activity != null)
                        activity.SetTag("txc.error_message", "bad");
                }
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new NoDirectActivityTaggingAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "TXC025");
    }

    [Fact]
    public async Task NoDirectProcessStart_FlagsCommandProcessStart()
    {
        const string source = """
            using System.Diagnostics;
            using DotMake.CommandLine;
            using TALXIS.CLI.Core.Shared;

            [CliCommand]
            public sealed class BadCommand : TxcLeafCommand
            {
                public void Execute()
                {
                    Process.Start("txc");
                }
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new NoDirectProcessStartAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "TXC026");
    }

    [Fact]
    public async Task MustUseWriteResultForMutations_FlagsMutativeCommandWithoutResultEnvelope()
    {
        const string source = """
            using TALXIS.CLI.Core;
            using TALXIS.CLI.Core.Shared;

            [CliIdempotent]
            public sealed class BadCommand : TxcLeafCommand
            {
                public void Execute()
                {
                }
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new MustUseWriteResultForMutationsAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "TXC027");
    }

    [Fact]
    public async Task MustUseWriteResultForMutations_AllowsMutativeCommandWithResultEnvelope()
    {
        const string source = """
            using TALXIS.CLI.Core;
            using TALXIS.CLI.Core.Shared;

            [CliIdempotent]
            public sealed class GoodCommand : TxcLeafCommand
            {
                public void Execute()
                {
                    OutputFormatter.WriteResult();
                }
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new MustUseWriteResultForMutationsAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "TXC027");
    }

    [Fact]
    public async Task NoWriteResultInReadOnly_FlagsReadOnlyResultEnvelope()
    {
        const string source = """
            using TALXIS.CLI.Core;
            using TALXIS.CLI.Core.Shared;

            [CliReadOnly]
            public sealed class BadCommand : TxcLeafCommand
            {
                public void Execute()
                {
                    OutputFormatter.WriteResult();
                }
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new NoWriteResultInReadOnlyAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "TXC028");
    }

    [Fact]
    public async Task NoDirectCallToolResult_FlagsObjectCreationOutsideFactory()
    {
        const string source = """
            using ModelContextProtocol.Protocol;

            public sealed class BadHandler
            {
                public CallToolResult Execute() => new CallToolResult();
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new NoDirectCallToolResultAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "TXC029");
    }

    [Fact]
    public async Task NoDirectCallToolResult_AllowsObjectCreationInsideFactory()
    {
        const string source = """
            using ModelContextProtocol.Protocol;

            public sealed class McpToolResultFactory
            {
                public CallToolResult Execute() => new CallToolResult();
            }
            """ + Stubs;

        var diagnostics = await RunAnalyzerAsync(source, new NoDirectCallToolResultAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "TXC029");
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string source, DiagnosticAnalyzer analyzer)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private const string Stubs = """
        namespace DotMake.CommandLine
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class CliCommandAttribute : System.Attribute { }
        }

        namespace TALXIS.CLI.Core
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class CliDestructiveAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class CliIdempotentAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class CliReadOnlyAttribute : System.Attribute { }

            public static class OutputFormatter
            {
                public static void WriteResult() { }
            }
        }

        namespace TALXIS.CLI.Core.Shared
        {
            public abstract class TxcLeafCommand { }
        }

        namespace ModelContextProtocol.Protocol
        {
            public sealed class CallToolResult { }
        }

        """;
}
