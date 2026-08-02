using System.Text;
using System.Text.Json;
using MdReader.Core;

return await Cli.RunAsync(args);

internal static class Cli
{
    private const int Success = 0;
    private const int Failure = 1;
    private const int InputError = 2;
    private const int ValidationFindings = 4;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return args.Length == 0 ? InputError : Success;
        }

        if (args.Contains("--version"))
        {
            Console.Out.WriteLine($"mdreader-convert {typeof(MarkdownRenderer).Assembly.GetName().Version?.ToString(3)}");
            return Success;
        }

        try
        {
            var options = ApplyConfig(Options.Parse(args));
            if (options.CheckDirectory is not null)
            {
                return await CheckDirectoryAsync(options);
            }

            if (options.Watch)
            {
                if (options.InputPath is null || options.UseStdin || options.UseStdout)
                {
                    return Error("--watch requires a file input and file output.", InputError);
                }

                return await WatchAsync(options);
            }

            return await ConvertOnceAsync(options);
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message, InputError);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Error(ex.Message, Failure);
        }
    }

    private static async Task<int> ConvertOnceAsync(Options options)
    {
        var (text, documentPath) = await ReadInputAsync(options);
        var renderer = new MarkdownRenderer();
        var result = renderer.Render(text, new RenderOptions
        {
            DocumentPath = documentPath,
            AllowRawHtml = options.AllowRawHtml,
            KeepRelativeImagePaths = true,
        });
        var diagnostics = result.Diagnostics;
        WriteDiagnostics(diagnostics, options.DiagnosticsJson);
        if (options.CheckOnly)
        {
            return diagnostics.Count == 0 ? Success : ValidationFindings;
        }

        var cssPath = Path.Combine(AppContext.BaseDirectory, "Web", "css", "reader.css");
        var css = File.Exists(cssPath) ? await File.ReadAllTextAsync(cssPath) : string.Empty;
        if (options.ContentWidth is { } width)
        {
            css += $"\n:root {{ --content-width: {width}px; }}\n";
        }

        if (options.ProjectCssPath is { } projectCss && File.Exists(projectCss))
        {
            css += "\n/* .mdreader.json project CSS */\n" + await File.ReadAllTextAsync(projectCss);
        }
        var title = result.Title ?? (documentPath is null ? "Markdown document" : Path.GetFileNameWithoutExtension(documentPath));
        var html = HtmlDocumentAssembler.BuildStandalone(result, css, title, options.Theme);
        if (options.Deterministic)
        {
            html = html.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        if (options.UseStdout)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            await Console.Out.WriteAsync(html);
        }
        else
        {
            var output = options.OutputPath ?? Path.ChangeExtension(documentPath!, ".html");
            await File.WriteAllTextAsync(output, html, new UTF8Encoding(false));
            Console.Error.WriteLine($"wrote {output}");
        }

        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? ValidationFindings : Success;
    }

    private static async Task<(string Text, string? DocumentPath)> ReadInputAsync(Options options)
    {
        if (options.UseStdin)
        {
            var text = await Console.In.ReadToEndAsync();
            var syntheticPath = options.BaseDirectory is null
                ? null
                : Path.Combine(Path.GetFullPath(options.BaseDirectory), "stdin.md");
            return (text, syntheticPath);
        }

        if (options.InputPath is null || !File.Exists(options.InputPath))
        {
            throw new ArgumentException($"file not found: {options.InputPath}");
        }

        var fullPath = Path.GetFullPath(options.InputPath);
        return (TextFileIO.Read(fullPath).Text, fullPath);
    }

    private static async Task<int> CheckDirectoryAsync(Options options)
    {
        var root = Path.GetFullPath(options.CheckDirectory!);
        if (!Directory.Exists(root))
        {
            return Error($"directory not found: {root}", InputError);
        }

        var diagnostics = new List<DocumentDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(FileTypes.IsMarkdown)
                     .Where(path => !HasIgnoredDirectory(root, path)))
        {
            diagnostics.AddRange(MarkdownDiagnostics.Analyze(TextFileIO.Read(file).Text, file));
        }

        WriteDiagnostics(diagnostics, options.DiagnosticsJson);
        return diagnostics.Count == 0 ? Success : ValidationFindings;
    }

    private static bool HasIgnoredDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar)
            .Any(part => part is ".git" or "bin" or "obj");
    }

    private static async Task<int> WatchAsync(Options options)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var input = Path.GetFullPath(options.InputPath!);
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(input)!, Path.GetFileName(input))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        var changed = new SemaphoreSlim(0, 1);
        void Signal(object? _, FileSystemEventArgs __) { if (changed.CurrentCount == 0) { changed.Release(); } }
        watcher.Changed += Signal;
        watcher.Created += Signal;
        watcher.Renamed += (_, _) => { if (changed.CurrentCount == 0) { changed.Release(); } };

        Console.Error.WriteLine($"watching {input}; press Ctrl+C to stop");
        while (!cancellation.IsCancellationRequested)
        {
            var code = await ConvertOnceAsync(options);
            if (code is Failure or InputError)
            {
                Console.Error.WriteLine("conversion failed; waiting for the next change");
            }

            try
            {
                await changed.WaitAsync(cancellation.Token);
                await Task.Delay(250, cancellation.Token);
                while (changed.Wait(0)) { }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return Success;
    }

    private static void WriteDiagnostics(IEnumerable<DocumentDiagnostic> diagnostics, bool json)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (json)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    code = diagnostic.Code,
                    severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                    diagnostic.Message,
                    path = diagnostic.DocumentPath,
                    diagnostic.Line,
                    diagnostic.Target,
                }));
            }
            else
            {
                Console.Error.WriteLine($"{diagnostic.DocumentPath}({diagnostic.Line}): {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code}: {diagnostic.Message}");
            }
        }
    }

    private static int Error(string message, int code)
    {
        Console.Error.WriteLine($"mdreader-convert: {message}");
        return code;
    }

    private static Options ApplyConfig(Options options)
    {
        var start = options.InputPath is { } input
            ? Path.GetDirectoryName(Path.GetFullPath(input))
            : options.BaseDirectory is { } baseDir ? Path.GetFullPath(baseDir) : Directory.GetCurrentDirectory();
        for (var directory = new DirectoryInfo(start!); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, ".mdreader.json");
            if (!File.Exists(path))
            {
                continue;
            }

            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            }) ?? new Config();
            var theme = config.Theme is "light" or "dark" ? config.Theme : options.Theme;
            return options with
            {
                Theme = options.ThemeWasExplicit ? options.Theme : theme,
                Deterministic = options.Deterministic || config.Deterministic,
                ContentWidth = config.ContentWidth is >= 320 and <= 10000 ? config.ContentWidth : null,
                ProjectCssPath = ResolveProjectCss(directory.FullName, config.CustomCss),
            };
        }

        return options;
    }

    private static string? ResolveProjectCss(string directory, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || Path.IsPathRooted(configured))
        {
            return null;
        }

        var resolved = Path.GetFullPath(Path.Combine(directory, configured));
        var root = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? resolved : null;
    }

    private static void PrintHelp() => Console.Out.WriteLine("""
        mdreader-convert — safe Markdown conversion and validation

        usage:
          mdreader-convert <input.md> [output.html] [options]
          mdreader-convert --stdin --stdout [--base-dir <dir>] [options]
          mdreader-convert --check-links <file-or-directory> [--diagnostics json]

        options:
          --stdin                 read Markdown from standard input
          --stdout                write HTML only to standard output
          --watch                 rebuild after local file changes
          --check-links <path>    validate local files, images, and anchors
          --diagnostics json      emit versioned JSON Lines diagnostics to stderr
          --theme light|dark      output theme (default: light)
          --allow-raw-html        allow sanitized raw HTML
          --base-dir <dir>        resolve stdin-relative assets from this directory
          --deterministic         normalize output for reproducible HTML
        """);

    private sealed record Options
    {
        public string? InputPath { get; init; }
        public string? OutputPath { get; init; }
        public string? BaseDirectory { get; init; }
        public string? CheckDirectory { get; init; }
        public string Theme { get; init; } = "light";
        public bool UseStdin { get; init; }
        public bool UseStdout { get; init; }
        public bool Watch { get; init; }
        public bool CheckOnly { get; init; }
        public bool DiagnosticsJson { get; init; }
        public bool AllowRawHtml { get; init; }
        public bool Deterministic { get; init; }
        public bool ThemeWasExplicit { get; init; }
        public int? ContentWidth { get; init; }
        public string? ProjectCssPath { get; init; }

        public static Options Parse(string[] args)
        {
            string? input = null, output = null, baseDir = null, check = null;
            var theme = "light";
            var stdin = false; var stdout = false; var watch = false; var json = false; var themeExplicit = false;
            var raw = false; var deterministic = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--stdin": stdin = true; break;
                    case "--stdout": stdout = true; break;
                    case "--watch": watch = true; break;
                    case "--allow-raw-html": raw = true; break;
                    case "--deterministic": deterministic = true; break;
                    case "--theme" when ++i < args.Length: theme = args[i]; themeExplicit = true; break;
                    case "--base-dir" when ++i < args.Length: baseDir = args[i]; break;
                    case "--diagnostics" when ++i < args.Length: json = args[i] == "json"; break;
                    case "--check-links" when ++i < args.Length: check = args[i]; break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException($"unknown or incomplete option: {args[i]}");
                        }

                        if (input is null)
                        {
                            input = args[i];
                        }
                        else if (output is null)
                        {
                            output = args[i];
                        }
                        else
                        {
                            throw new ArgumentException("too many positional arguments");
                        }

                        break;
                }
            }

            if (stdin && input is not null)
            {
                throw new ArgumentException("use either a file or --stdin, not both");
            }

            if (!stdin && input is null && check is null)
            {
                throw new ArgumentException("an input file or --stdin is required");
            }

            if (stdout && output is not null)
            {
                throw new ArgumentException("use either an output file or --stdout, not both");
            }

            if (theme is not ("light" or "dark"))
            {
                throw new ArgumentException("theme must be light or dark");
            }

            if (check is not null && File.Exists(check))
            {
                input = check;
            }

            return new Options
            {
                InputPath = input,
                OutputPath = output,
                BaseDirectory = baseDir,
                CheckDirectory = check is not null && Directory.Exists(check) ? check : null,
                CheckOnly = check is not null && File.Exists(check),
                Theme = theme,
                UseStdin = stdin,
                UseStdout = stdout,
                Watch = watch,
                DiagnosticsJson = json,
                AllowRawHtml = raw,
                Deterministic = deterministic,
                ThemeWasExplicit = themeExplicit,
            };
        }
    }

    private sealed record Config
    {
        public int Version { get; init; } = 1;
        public string? Theme { get; init; }
        public bool Deterministic { get; init; }
        public int? ContentWidth { get; init; }
        public string? CustomCss { get; init; }
    }
}
