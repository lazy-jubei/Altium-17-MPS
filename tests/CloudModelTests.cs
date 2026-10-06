using Altium17PartSearch.PartSearch;

static void Require(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
static async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine("PASS " + name); return; }
    throw new Exception(name);
}
Require(CloudModelPolicy.Matches(" lm358n/nopb ", " TEXAS INSTRUMENTS ", "LM358N/NOPB", "Texas Instruments"), "exact match ignores surrounding whitespace and case");
Require(!CloudModelPolicy.Matches("LM358N", "Texas Instruments", "LM358N/NOPB", "Texas Instruments"), "rejects different ordering suffix");
Require(!CloudModelPolicy.Matches("LM358N", "onsemi", "LM358N", "Texas Instruments"), "rejects wrong manufacturer");
Require(!CloudModelPolicy.Matches("LM358N", "", "LM358N", "Texas Instruments"), "missing manufacturer does not qualify");
Require(!CloudModelPolicy.Matches("", "onsemi", "", "onsemi"), "missing MPN does not qualify");
Require(CloudModelPolicy.Matches("EP4CE40F23C8N", "Altera", "EP4CE40F23C8N", "Intel / Altera"), "legacy Altera name matches Ciiva's composite FPGA brand");
Require(!CloudModelPolicy.Matches("EP4CE40F23C8N", "Intel", "EP4CE40F23C8N", "Altera"), "Intel alone is not treated as Altera");
Require(CloudModelPolicy.QuoteQuery("LM358N/NOPB") == "\"LM358N/NOPB\"", "MPN punctuation is a literal search phrase");
Require(CloudModelPolicy.QuoteQuery("x\" OR * \\y") == "\"x\\\" OR * \\\\y\"", "escapes search operators and quoted input");
string vault = Guid.NewGuid().ToString();
CloudModelPolicy.Revision Rev(string id, string source = null) => new() { VaultGuid = source ?? vault, RevisionGuid = id };
var root = Rev(Guid.NewGuid().ToString());
var symbol = Rev(Guid.NewGuid().ToString());
var footprint = Rev(Guid.NewGuid().ToString());
var step = Rev(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
var graph = new Dictionary<string, CloudModelPolicy.Revision[]> {
    [root.Key] = new[] { symbol, footprint, footprint }, [symbol.Key] = new[] { root },
    [footprint.Key] = new[] { step }, [step.Key] = Array.Empty<CloudModelPolicy.Revision>()
};
var visited = new List<string>();
await CloudModelPolicy.DownloadGraphAsync(root, node => {
    visited.Add(node.Key); return Task.FromResult<IEnumerable<CloudModelPolicy.Revision>>(graph[node.Key]);
}, CancellationToken.None);
Require(visited.Count == 4 && visited.Contains(step.Key), "downloads linked 3D model across Vaults and stops duplicate/cyclic links");
await Reject<InvalidOperationException>(() => CloudModelPolicy.DownloadGraphAsync(root,
    _ => Task.FromResult<IEnumerable<CloudModelPolicy.Revision>>(Enumerable.Range(0, 101).Select(i => Rev(Guid.NewGuid().ToString()))), CancellationToken.None), "bounds revision fanout");
await Reject<InvalidOperationException>(() => CloudModelPolicy.DownloadGraphAsync(Rev("not-a-guid"),
    _ => Task.FromResult<IEnumerable<CloudModelPolicy.Revision>>(Array.Empty<CloudModelPolicy.Revision>()), CancellationToken.None), "rejects invalid revision ID before download");
await Reject<IOException>(() => CloudModelPolicy.DownloadGraphAsync(root,
    _ => throw new IOException("download failed"), CancellationToken.None), "failed dependency aborts import");
using var canceled = new CancellationTokenSource(); canceled.Cancel();
await Reject<OperationCanceledException>(() => CloudModelPolicy.DownloadGraphAsync(root,
    _ => throw new Exception("should not start"), canceled.Token), "cancel before download");
using var during = new CancellationTokenSource();
int calls = 0;
await Reject<OperationCanceledException>(() => CloudModelPolicy.DownloadGraphAsync(root,
    _ => { calls++; during.Cancel(); return Task.FromResult<IEnumerable<CloudModelPolicy.Revision>>(new[] { symbol }); }, during.Token), "cancel prevents subsequent dependency download");
Require(calls == 1, "no calls after cancellation");
await CloudModelPolicy.WaitAsync(() => true, CancellationToken.None, TimeSpan.FromSeconds(1));
await Reject<TimeoutException>(() => CloudModelPolicy.WaitAsync(() => false, CancellationToken.None, TimeSpan.Zero), "native request timeout");
await Reject<OperationCanceledException>(() => CloudModelPolicy.WaitAsync(() => true, canceled.Token, TimeSpan.FromSeconds(1)), "completed request still honors cancellation");
using var pending = new CancellationTokenSource(20);
await Reject<OperationCanceledException>(() => CloudModelPolicy.WaitAsync(() => false, pending.Token, TimeSpan.FromSeconds(1)), "pending native request can be canceled");

string modelCache = Path.Combine(Path.GetTempPath(), "mps-model-test-" + Guid.NewGuid());
try {
    Directory.CreateDirectory(Path.Combine(modelCache, "Released"));
    Directory.CreateDirectory(Path.Combine(modelCache, "Design"));
    string released = Path.Combine(modelCache, "Released", "Package.PcbLib");
    File.WriteAllText(released, "test");
    File.WriteAllText(Path.Combine(modelCache, "Design", "Package.PcbLib"), "test");
    Require(CloudModelPolicy.FindModelFile(modelCache, "PACKAGE", "PCBLIB") == released, "resolves released CAD file with case-insensitive type/name");
    Require(CloudModelPolicy.FindModelFile(released, "Package", "PCBLIB") == released, "accepts native cache returning a file");
    Require(CloudModelPolicy.FindModelFile(modelCache, "different-entity-name", "PCBLIB") == released, "one library may contain an entity with a different name");
    await Reject<InvalidOperationException>(() => { CloudModelPolicy.FindModelFile(modelCache, "Package", "PCB3DLIB"); return Task.CompletedTask; }, "missing model type aborts import");
    File.WriteAllText(Path.Combine(modelCache, "Released", "Other.PcbLib"), "test");
    await Reject<InvalidOperationException>(() => { CloudModelPolicy.FindModelFile(modelCache, "unknown", "PCBLIB"); return Task.CompletedTask; }, "ambiguous cached libraries abort import");
    Require(CloudModelPolicy.FindModelFile(modelCache, "Package", "PCBLIB") == released, "exact filename wins over unrelated libraries");
} finally { Directory.Delete(modelCache, true); }
await Reject<InvalidOperationException>(() => { CloudModelPolicy.FindModelFile(modelCache, "Package", "PCBLIB"); return Task.CompletedTask; }, "missing cache directory aborts import");
