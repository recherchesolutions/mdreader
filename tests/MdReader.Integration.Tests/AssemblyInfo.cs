// Each test launches a real mdreader.exe with WebView2; running them serially
// keeps resource usage sane and removes cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
