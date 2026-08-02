using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MdReader.Core;

// `--quick` runs a fast stopwatch pass; `--check` additionally enforces the
// budgets in benchmarks/budgets.json (non-zero exit on regression — used by CI).
// No arguments runs the full BenchmarkDotNet suite.
if (args.Contains("--quick") || args.Contains("--check"))
{
    return QuickBench.Run(enforceBudgets: args.Contains("--check"));
}

BenchmarkRunner.Run<RenderBenchmarks>();
return 0;

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
    /// <summary>
    /// Stopwatch pass over the deterministic render pipeline. With
    /// <paramref name="enforceBudgets"/>, results are compared against
    /// benchmarks/budgets.json (baseline ×2 headroom — regression guard, not a
    /// micro-benchmark) and the process exits non-zero on any breach.
    /// GUI cold-start is intentionally NOT gated here: it is environment-
    /// sensitive; measure it with tools/measure-startup.ps1 and track manually.
    /// </summary>
    public static int Run(bool enforceBudgets)
    {
        var renderer = new MarkdownRenderer();
        _ = renderer.Render("warm up");

        var results = new Dictionary<string, double>
        {
            ["pipelineBuildMs"] = Measure("pipeline build", 5, () => MarkdownPipelineFactory.Build(allowRawHtml: false)),
            ["render100KBMs"] = Measure("render 100KB", 10, Render(renderer, 100 * 1024)),
            ["render1MBMs"] = Measure("render 1MB", 5, Render(renderer, 1024 * 1024)),
            ["render10MBMs"] = Measure("render 10MB", 2, Render(renderer, 10 * 1024 * 1024)),
        };

        if (!enforceBudgets)
        {
            return 0;
        }

        var budgetPath = FindBudgetsFile();
        if (budgetPath is null)
        {
            Console.Error.WriteLine("budgets.json not found");
            return 2;
        }

        var budgets = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(
            File.ReadAllText(budgetPath))!;

        var failed = false;
        foreach (var (key, budget) in budgets)
        {
            if (!results.TryGetValue(key, out var actual))
            {
                continue;
            }

            var ok = actual <= budget;
            Console.WriteLine($"budget {key}: {actual:F0}ms / {budget:F0}ms {(ok ? "OK" : "EXCEEDED")}");
            failed |= !ok;
        }

        return failed ? 1 : 0;
    }

    private static Func<object> Render(MarkdownRenderer renderer, int bytes)
    {
        var doc = BenchDocs.Build(bytes);
        return () => renderer.Render(doc);
    }

    private static string? FindBudgetsFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "benchmarks", "budgets.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static double Measure(string name, int iterations, Func<object> action)
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
        return best;
    }
}
