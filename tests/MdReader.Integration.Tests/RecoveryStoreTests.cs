using FluentAssertions;
using MdReader.App.Services;

namespace MdReader.Integration.Tests;

/// <summary>Unit tests for crash-recovery snapshot storage (sandboxed root dir).</summary>
public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _root;
    private readonly RecoveryStore _store;

    public RecoveryStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mdreader-recovery-{Guid.NewGuid():N}");
        _store = new RecoveryStore(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Save_list_and_remove_round_trip()
    {
        _store.Save(@"C:\docs\a.md", "unsaved text A");
        _store.Save(@"C:\docs\b.md", "unsaved text B");

        var entries = _store.ListAndPrune();
        entries.Should().HaveCount(2);
        entries.Select(e => e.Text).Should().BeEquivalentTo(["unsaved text A", "unsaved text B"]);

        _store.Remove(@"C:\docs\a.md");
        _store.ListAndPrune().Should().ContainSingle().Which.OriginalPath.Should().Be(@"C:\docs\b.md");
    }

    [Fact]
    public void Update_overwrites_same_document_snapshot()
    {
        _store.Save(@"C:\docs\a.md", "v1");
        _store.Save(@"C:\docs\a.md", "v2");

        _store.ListAndPrune().Should().ContainSingle().Which.Text.Should().Be("v2");
    }

    [Fact]
    public void Path_matching_is_case_insensitive()
    {
        _store.Save(@"C:\Docs\A.md", "text");
        _store.Remove(@"c:\docs\a.md");
        _store.ListAndPrune().Should().BeEmpty();
    }

    [Fact]
    public void Corrupt_snapshot_is_pruned_not_fatal()
    {
        _store.Save(@"C:\docs\good.md", "fine");
        File.WriteAllText(Path.Combine(_root, "deadbeef.json"), "{ not valid json");

        var entries = _store.ListAndPrune();

        entries.Should().ContainSingle();
        Directory.EnumerateFiles(_root, "*.json").Should().HaveCount(1, "the corrupt file must be removed");
    }

    [Fact]
    public void Entry_cap_refuses_new_snapshots_but_updates_existing()
    {
        for (var i = 0; i < RecoveryStore.MaxEntries; i++)
        {
            _store.Save($@"C:\docs\file{i}.md", $"text {i}");
        }

        _store.Save(@"C:\docs\overflow.md", "should not be stored");
        _store.Save(@"C:\docs\file0.md", "updated");

        var entries = _store.ListAndPrune();
        entries.Should().HaveCount(RecoveryStore.MaxEntries);
        entries.Should().NotContain(e => e.OriginalPath.EndsWith("overflow.md"));
        entries.Single(e => e.OriginalPath.EndsWith("file0.md")).Text.Should().Be("updated");
    }

    [Fact]
    public void Missing_root_lists_empty()
    {
        new RecoveryStore(Path.Combine(_root, "never-created")).ListAndPrune().Should().BeEmpty();
    }
}
