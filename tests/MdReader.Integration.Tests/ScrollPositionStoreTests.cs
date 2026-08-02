using FluentAssertions;
using MdReader.App.Services;

namespace MdReader.Integration.Tests;

public sealed class ScrollPositionStoreTests : IDisposable
{
    private readonly string _path;

    public ScrollPositionStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"mdreader-scroll-{Guid.NewGuid():N}.json");
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Round_trips_across_instances()
    {
        var store = new ScrollPositionStore(_path, pruneMissingFiles: false);
        store.Set(@"C:\docs\a.md", 42);
        store.Set(@"C:\docs\b.md", 7);
        store.Save();

        var reloaded = new ScrollPositionStore(_path, pruneMissingFiles: false);
        reloaded.Get(@"C:\docs\a.md").Should().Be(42);
        reloaded.Get(@"c:\DOCS\b.md").Should().Be(7, "path lookup is case-insensitive");
        reloaded.Get(@"C:\docs\unknown.md").Should().BeNull();
    }

    [Fact]
    public void Prunes_entries_for_missing_files_on_load()
    {
        var real = Path.Combine(Path.GetTempPath(), $"mdreader-real-{Guid.NewGuid():N}.md");
        File.WriteAllText(real, "# x");
        try
        {
            var store = new ScrollPositionStore(_path, pruneMissingFiles: false);
            store.Set(real, 10);
            store.Set(@"C:\definitely\missing\file.md", 99);
            store.Save();

            var reloaded = new ScrollPositionStore(_path, pruneMissingFiles: true);
            reloaded.Get(real).Should().Be(10);
            reloaded.Get(@"C:\definitely\missing\file.md").Should().BeNull();
        }
        finally
        {
            File.Delete(real);
        }
    }

    [Fact]
    public void Lru_bound_evicts_oldest()
    {
        var store = new ScrollPositionStore(_path, pruneMissingFiles: false);
        for (var i = 0; i < ScrollPositionStore.MaxEntries + 10; i++)
        {
            store.Set($@"C:\docs\f{i:D4}.md", i + 1);
        }

        store.Get(@"C:\docs\f0000.md").Should().BeNull("the oldest entries are evicted");
        store.Get($@"C:\docs\f{ScrollPositionStore.MaxEntries + 9:D4}.md").Should().NotBeNull();
    }

    [Fact]
    public void Corrupt_file_starts_fresh()
    {
        File.WriteAllText(_path, "{ nope");
        var store = new ScrollPositionStore(_path, pruneMissingFiles: false);
        store.Get(@"C:\docs\a.md").Should().BeNull();
        store.Set(@"C:\docs\a.md", 3);
        store.Save();
        new ScrollPositionStore(_path, pruneMissingFiles: false).Get(@"C:\docs\a.md").Should().Be(3);
    }
}
