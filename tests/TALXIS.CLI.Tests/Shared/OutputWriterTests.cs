using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Shared;

public class OutputWriterTests
{
    [Fact]
    public void RedirectTo_CapturesOutput()
    {
        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw))
        {
            OutputWriter.WriteLine("hello");
            OutputWriter.Write("world");
        }

        Assert.Equal("hello" + System.Environment.NewLine + "world", sw.ToString());
    }

    [Fact]
    public void RedirectTo_RestoresOnDispose()
    {
        var first = new StringWriter();
        var second = new StringWriter();

        using (OutputWriter.RedirectTo(first))
        {
            OutputWriter.WriteLine("first");

            using (OutputWriter.RedirectTo(second))
            {
                OutputWriter.WriteLine("second");
            }

            // After inner dispose, writes should go back to first
            OutputWriter.WriteLine("back-to-first");
        }

        Assert.Equal("first" + System.Environment.NewLine + "back-to-first" + System.Environment.NewLine, first.ToString());
        Assert.Equal("second" + System.Environment.NewLine, second.ToString());
    }

    [Fact]
    public void NestedRedirect_RestoresCorrectly()
    {
        var writers = new StringWriter[3];
        for (int i = 0; i < 3; i++) writers[i] = new StringWriter();

        using (OutputWriter.RedirectTo(writers[0]))
        {
            OutputWriter.WriteLine("L0");
            using (OutputWriter.RedirectTo(writers[1]))
            {
                OutputWriter.WriteLine("L1");
                using (OutputWriter.RedirectTo(writers[2]))
                {
                    OutputWriter.WriteLine("L2");
                }
                OutputWriter.WriteLine("L1-again");
            }
            OutputWriter.WriteLine("L0-again");
        }

        Assert.Contains("L0", writers[0].ToString());
        Assert.Contains("L0-again", writers[0].ToString());
        Assert.Contains("L1", writers[1].ToString());
        Assert.Contains("L1-again", writers[1].ToString());
        Assert.Contains("L2", writers[2].ToString());
    }

    [Fact]
    public void WriteLine_EmptyOverload_WritesNewline()
    {
        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw))
        {
            OutputWriter.WriteLine();
        }

        Assert.Equal(System.Environment.NewLine, sw.ToString());
    }

    [Fact]
    public void RedirectTo_ThrowsForNullWriter()
    {
        Assert.Throws<ArgumentNullException>(() => OutputWriter.RedirectTo(null!));
    }
}
