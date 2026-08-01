using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MdReader.Core;

// `--quick` runs a fast stopwatch pass (used by CI and the phase-gate reports);
// no arguments runs the full BenchmarkDotNet suite.
if (args.Contains("--quick"))
{
    QuickBench.Run();
    return;
}

BenchmarkRunner.Run<RenderBenchmarks>();

[MemoryDiagnoser]
public class RenderBenchmarks
{
    private readonly MarkdownRenderer _renderer = new();
    private string _doc100Kb = string.Empty;
    private string _doc5Mb = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _doc100Kb = BenchDocs.Build(100 * 1024);
        _doc5Mb = BenchDocs.Build(5 * 1024 * 1024);
        _ = _renderer.Render("warm up"); // pipeline construction is measured separately
    }

    /// <summary>§6 target: re-render of a 100KB document &lt; 150ms.</summary>
    [Benchmark]
    public RenderResult Render100KB() => _renderer.Render(_doc100Kb);

    /// <summary>§6: a 5MB document must not freeze the app.</summary>
    [Benchmark]
    public RenderResult Render5MB() => _renderer.Render(_doc5Mb);

    /// <summary>Pipeline construction cost (paid once at startup, cached after).</summary>
    [Benchmark]
    public object PipelineBuild() => MarkdownPipelineFactory.Build(allowRawHtml: false);
}

public static class BenchDocs
{
    public static string Build(int targetBytes)
    {
        var section = """
            ## Section heading

            Some paragraph text with **bold**, *italic*, `inline code`, a [link](https://example.com),
            and :rocket: emoji. Occasionally math $x^2$ appears.

            - item one
            - item two with `code`
            - [ ] a task

            | col A | col B |
            |-------|-------|
            | 1     | 2     |

            ```csharp
            var x = 42;
            ```


            """;
        var sb = new StringBuilder("# Benchmark document\n\n");
        while (sb.Length < targetBytes)
        {
            sb.Append(section);
        }

        return sb.ToString();
    }
}

public static class QuickBench
{
    public static void Run()
    {
        var renderer = new MarkdownRenderer();
        _ = renderer.Render("warm up");

        Measure("pipeline build", 5, () => MarkdownPipelineFactory.Build(allowRawHtml: false));
        var doc100 = BenchDocs.Build(100 * 1024);
        Measure("render 100KB (target < 150ms)", 10, () => renderer.Render(doc100));
        var doc5M = BenchDocs.Build(5 * 1024 * 1024);
        Measure("render 5MB", 3, () => renderer.Render(doc5M));
    }

    private static void Measure(string name, int iterations, Func<object> action)
    {
        _ = action(); // warm
        var sw = new Stopwatch();
        var best = double.MaxValue;
        for (var i = 0; i < iterations; i++)
        {
            sw.Restart();
            _ = action();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        Console.WriteLine($"{name}: {best:F1}ms (best of {iterations})");
    }
}
