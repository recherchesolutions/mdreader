using System.Diagnostics;
using FluentAssertions;

namespace MdReader.Integration.Tests;

[Trait("suite", "blocking")]
public sealed class CliPipelineTests
{
    private static string CliDll => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MdReader.Cli", "bin", "Release", "net10.0", "mdreader-convert.dll"));

    [Fact]
    public async Task Stdin_stdout_are_clean_and_deterministic()
    {
        var first = await RunAsync("--stdin --stdout --deterministic", "# Hello\n\nWorld");
        var second = await RunAsync("--stdin --stdout --deterministic", "# Hello\n\nWorld");

        first.ExitCode.Should().Be(0);
        first.Stdout.Should().StartWith("<!DOCTYPE html>");
        first.Stdout.Should().Be(second.Stdout);
        first.Stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Json_diagnostics_use_stderr_and_distinct_exit_code()
    {
        var result = await RunAsync("--stdin --base-dir . --stdout --diagnostics json", "![x](missing.png)");

        result.ExitCode.Should().Be(0, "warnings do not prevent conversion");
        result.Stdout.Should().StartWith("<!DOCTYPE html>");
        result.Stderr.Should().Contain("\"schemaVersion\":1").And.Contain("\"code\":\"MD001\"");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string arguments, string stdin)
    {
        var start = new ProcessStartInfo("dotnet", $"\"{CliDll}\" {arguments}")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }
}
