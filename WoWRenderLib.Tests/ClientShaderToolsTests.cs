using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWViewer.ClientShaderTools;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class ClientShaderToolsTests
{
    private static readonly byte[] PixelProgram = Words(0xFFFF0300, 0x02000001,
        0x800F0800, 0xA0E40000, 0x0000FFFF); // ps_3_0; mov oC0, c0; end.

    [TestMethod]
    public void ParserRetainsEveryPermutationAndFourByteAlignment()
    {
        var bytes = Container([1, 2, 3, 4, 5], [8, 9, 10, 11], []);
        var records = BlsParser.Parse(bytes);
        Assert.AreEqual(3, records.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, records[0].Bytecode);
        CollectionAssert.AreEqual(new byte[] { 8, 9, 10, 11 }, records[1].Bytecode);
        Assert.AreEqual(36, records[1].RecordOffset);
        Assert.AreEqual(52, records[1].PayloadOffset);
        Assert.AreEqual(1, records[1].Ordinal);
        Assert.AreEqual(101u, records[1].Metadata0);
        Assert.AreEqual(201u, records[1].Metadata1);
        Assert.AreEqual((ushort)301, records[1].Metadata2);
        Assert.AreEqual((ushort)401, records[1].Metadata3);
        Assert.AreEqual(0, records[2].Bytecode.Length);
    }

    [TestMethod]
    public void EmptyPlaceholderAndZeroRecordContainerAreAccepted()
    {
        Assert.AreEqual(0, BlsParser.Parse([]).Count);
        Assert.AreEqual(0, BlsParser.Parse(Container()).Count);
    }

    [TestMethod]
    public void TruncatedHeadersPayloadsAndPaddingFailClosed()
    {
        var bytes = Container([1, 2, 3, 4, 5]);
        foreach (var length in new[] { 1, 11, 12, 27, 30, 33, 35 })
            Assert.ThrowsException<InvalidDataException>(() => BlsParser.Parse(bytes.AsSpan(0, length)), $"Length {length}");
    }

    [TestMethod]
    public void InvalidMagicVersionCountAndTrailingBytesAreRejected()
    {
        var badMagic = Container();
        badMagic[0] = 0;
        var badVersion = Container();
        badVersion[4] = 4;
        var badCount = Container();
        badCount[8] = 255;
        var badLength = Container([1]);
        Array.Fill(badLength, (byte)255, 24, 4);
        foreach (var bytes in new[] { badMagic, badVersion, badCount, badLength, Container().Concat(new byte[] { 0 }).ToArray() })
            Assert.ThrowsException<InvalidDataException>(() => BlsParser.Parse(bytes));
    }

    [TestMethod]
    public void NativeDisassemblerReadsSyntheticShaderModelThreeBytecode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The native DX9 disassembler requires Windows.");
            return;
        }
        var assembly = new Dx9Disassembler().Disassemble(PixelProgram);
        StringAssert.Contains(assembly, "ps_3_0");
        StringAssert.Contains(assembly, "mov oC0, c0");
    }

    [TestMethod]
    public void CachePreservesSelectorsButDisassemblesDuplicatePayloadOnlyOnce()
    {
        using var fixture = new CacheFixture();
        fixture.Source("pixel/ps_3_0/first.bls", Container(PixelProgram, PixelProgram));
        fixture.Source("pixel/ps_3_0/second.bls", Container(PixelProgram));
        var report = fixture.Run();
        Assert.AreEqual(0, report.Errors.Count);
        Assert.AreEqual(3, report.Permutations);
        Assert.AreEqual(1, report.UniquePrograms);
        Assert.AreEqual(1, fixture.Disassembler.Calls);
        var records = fixture.Manifest().Files.SelectMany(f => f.Permutations).ToArray();
        Assert.AreEqual(records[0].AssemblyPath, records[2].AssemblyPath);
        Assert.AreNotEqual(records[0].Metadata0, records[1].Metadata0);
        Assert.AreEqual("ps_3_0", records[0].ShaderProfile);
        var summary = File.ReadAllText(Path.Combine(fixture.Output, records[0].SummaryPath));
        StringAssert.Contains(summary, "oC0");
        StringAssert.Contains(summary, "c0");
    }

    [TestMethod]
    public void WarmCacheDoesNotReadSourceDisassembleOrRewriteArtifacts()
    {
        using var fixture = new CacheFixture();
        var source = fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        var timestamps = Directory.GetFiles(fixture.Output, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.GetLastWriteTimeUtc);
        using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        var report = fixture.Run();
        Assert.AreEqual(0, report.Processed);
        Assert.AreEqual(1, report.Reused);
        Assert.AreEqual(0, report.Disassembled);
        Assert.AreEqual(1, fixture.Disassembler.Calls);
        foreach (var pair in timestamps)
            Assert.AreEqual(pair.Value, File.GetLastWriteTimeUtc(pair.Key), pair.Key);
    }

    [TestMethod]
    public void MissingAssemblyIsRepairedFromCachedBinaryWithSourceLocked()
    {
        using var fixture = new CacheFixture();
        var source = fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        File.Delete(Path.Combine(fixture.Output, fixture.Manifest().Files[0].Permutations[0].AssemblyPath));
        using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        var report = fixture.Run();
        Assert.AreEqual(0, report.Processed);
        Assert.AreEqual(1, report.Reused);
        Assert.AreEqual(1, report.Disassembled);
        Assert.AreEqual(0, report.Errors.Count);
    }

    [TestMethod]
    public void HashVerificationDetectsSourceChangesWithPreservedSizeAndTimestamp()
    {
        using var fixture = new CacheFixture();
        var source = fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        var timestamp = File.GetLastWriteTimeUtc(source);
        var changed = (byte[])PixelProgram.Clone();
        changed[12] = 1;
        File.WriteAllBytes(source, Container(changed));
        File.SetLastWriteTimeUtc(source, timestamp);
        var report = fixture.Run(verifyHashes: true);
        Assert.AreEqual(1, report.Processed);
        Assert.AreEqual(1, report.Disassembled);
        Assert.AreEqual(2, fixture.Disassembler.Calls);
    }

    [TestMethod]
    public void HashVerificationRepairsCorruptedCachedBinaryAndAssembly()
    {
        using var fixture = new CacheFixture();
        fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        var record = fixture.Manifest().Files[0].Permutations[0];
        File.WriteAllBytes(Path.Combine(fixture.Output, record.BinaryPath), [0, 0]);
        File.WriteAllText(Path.Combine(fixture.Output, record.AssemblyPath), "damaged");
        var report = fixture.Run(verifyHashes: true);
        Assert.AreEqual(0, report.Errors.Count);
        Assert.AreEqual(1, report.Disassembled);
        CollectionAssert.AreEqual(PixelProgram, File.ReadAllBytes(Path.Combine(fixture.Output, record.BinaryPath)));
        StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.Output, record.AssemblyPath)), "ps_3_0");
    }

    [TestMethod]
    public void EmptyFilesAndPermutationsRemainVisibleInManifest()
    {
        using var fixture = new CacheFixture();
        fixture.Source("pixel/ps_3_0/placeholder.bls", []);
        fixture.Source("pixel/ps_3_0/empty-permutation.bls", Container(Array.Empty<byte>()));
        var report = fixture.Run();
        Assert.AreEqual(1, report.EmptyFiles);
        Assert.AreEqual(0, report.Errors.Count);
        Assert.AreEqual(0, report.UniquePrograms);
        Assert.IsTrue(fixture.Manifest().Files.Any(f => f.Status == "empty-file"));
        Assert.IsTrue(fixture.Manifest().Files.Any(f => f.Permutations.Any(p => p.Status == "empty-permutation")));
    }

    [TestMethod]
    public void Sm3ScopeRetainsEffectsAndAllDx9AddsOnlyD3dFallbackProfiles()
    {
        using var fixture = new CacheFixture();
        fixture.Source("vertex/vs_3_0/test.bls", Container(PixelProgram));
        fixture.Source("vertex/vs_2_0/test.bls", Container(PixelProgram));
        fixture.Source("vertex/arbvp1/test.bls", Container(PixelProgram));
        fixture.Source("effects/Particle.wfx", Encoding.UTF8.GetBytes("Effect(Particle)\n{\n}\n"));
        Assert.AreEqual(2, fixture.Run().Files);
        Assert.AreEqual(3, fixture.Run(profile: "AllDx9").Files);
        Assert.IsTrue(fixture.Manifest().Files.Any(f => f.EffectNames.Contains("Particle")));
        Assert.IsFalse(fixture.Manifest().Files.Any(f => f.ClientPath.Contains("arbvp1")));
    }

    [TestMethod]
    public void InvalidContainerReportsFailureWithoutBlockingOtherShaderExports()
    {
        using var fixture = new CacheFixture();
        fixture.Source("pixel/ps_3_0/invalid.bls", [1, 2]);
        fixture.Source("pixel/ps_3_0/valid.bls", Container(PixelProgram));
        var report = fixture.Run();
        Assert.AreEqual(1, report.Errors.Count);
        Assert.AreEqual(1, report.Disassembled);
        Assert.IsTrue(fixture.Manifest().Files.Any(f => f.Status == "error"));
    }

    [TestMethod]
    public void SwitchingScopesDoesNotDiscardOrExtractFallbackRecordsAgain()
    {
        using var fixture = new CacheFixture();
        fixture.Source("vertex/vs_3_0/test.bls", Container(PixelProgram));
        var fallback = fixture.Source("vertex/vs_2_0/test.bls", Container(PixelProgram));
        fixture.Run(profile: "AllDx9");
        using var locked = new FileStream(fallback, FileMode.Open, FileAccess.Read, FileShare.None);
        var sm3 = fixture.Run();
        Assert.AreEqual(1, sm3.Files);
        Assert.AreEqual(2, fixture.Manifest().Files.Length);
        var all = fixture.Run(profile: "AllDx9");
        Assert.AreEqual(0, all.Processed);
        Assert.AreEqual(2, all.Reused);
        Assert.AreEqual(0, all.Disassembled);
    }

    [TestMethod]
    public void ChangedSelectorsReuseExistingAssemblyWithoutLosingItsIntegrityHash()
    {
        using var fixture = new CacheFixture();
        var source = fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        var timestamp = File.GetLastWriteTimeUtc(source);
        var changed = Container(PixelProgram);
        changed[12] = 42;
        File.WriteAllBytes(source, changed);
        File.SetLastWriteTimeUtc(source, timestamp.AddSeconds(1));
        var updated = fixture.Run();
        Assert.AreEqual(1, updated.Processed);
        Assert.AreEqual(0, updated.Disassembled);
        Assert.AreEqual(42u, fixture.Manifest().Files[0].Permutations[0].Metadata0);
        Assert.AreEqual(0, fixture.Run(verifyHashes: true).Disassembled);
        Assert.AreEqual(1, fixture.Disassembler.Calls);
    }

    [TestMethod]
    public void ForceRebuildsManifestsAndDisassemblerIdentityInvalidatesOutputs()
    {
        using var fixture = new CacheFixture();
        fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        Assert.AreEqual(1, fixture.Run(force: true).Disassembled);
        fixture.Disassembler.Identity = "test-assembler-v2";
        Assert.AreEqual(1, fixture.Run().Disassembled);
        Assert.AreEqual(3, fixture.Disassembler.Calls);
    }

    [TestMethod]
    public void ForcedSm3RefreshRetainsCachedFallbacksForLaterScopeSwitch()
    {
        using var fixture = new CacheFixture();
        fixture.Source("vertex/vs_3_0/test.bls", Container(PixelProgram));
        var fallback = fixture.Source("vertex/vs_2_0/test.bls", Container(PixelProgram));
        fixture.Run(profile: "AllDx9");
        using var locked = new FileStream(fallback, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.AreEqual(1, fixture.Run(force: true).Disassembled);
        Assert.AreEqual(2, fixture.Manifest().Files.Length);
        var all = fixture.Run(profile: "AllDx9");
        Assert.AreEqual(0, all.Processed);
        Assert.AreEqual(2, all.Reused);
        Assert.AreEqual(0, all.Disassembled);
    }

    [TestMethod]
    public void ForceRecoversFromMalformedManifest()
    {
        using var fixture = new CacheFixture();
        fixture.Source("pixel/ps_3_0/test.bls", Container(PixelProgram));
        fixture.Run();
        File.WriteAllText(Path.Combine(fixture.Output, "manifest.json"), "{broken");
        Assert.ThrowsException<JsonException>(() => fixture.Run());
        var report = fixture.Run(force: true);
        Assert.AreEqual(0, report.Errors.Count);
        Assert.AreEqual(1, report.Processed);
        Assert.AreEqual(1, report.Disassembled);
        Assert.AreEqual(1, fixture.Manifest().Files.Length);
    }

    [TestMethod]
    public void OverlappingSourceAndOutputDirectoriesAreRejected()
    {
        using var fixture = new CacheFixture();
        Assert.ThrowsException<ArgumentException>(() => new ShaderCache(fixture.Disassembler)
            .Run(fixture.Input, Path.Combine(fixture.Input, "cache")));
        Assert.ThrowsException<ArgumentException>(() => new ShaderCache(fixture.Disassembler)
            .Run(fixture.Input, fixture.Root));
    }

    private static byte[] Words(params uint[] words) => words.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] Container(params byte[][] payloads)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(BlsParser.Magic);
        writer.Write(BlsParser.FormatTag);
        writer.Write((uint)payloads.Length);
        for (var i = 0; i < payloads.Length; i++)
        {
            writer.Write((uint)(100 + i));
            writer.Write((uint)(200 + i));
            writer.Write((ushort)(300 + i));
            writer.Write((ushort)(400 + i));
            writer.Write((uint)payloads[i].Length);
            writer.Write(payloads[i]);
            while (stream.Length % 4 != 0)
                writer.Write((byte)0xCC);
        }
        return stream.ToArray();
    }

    private sealed class FakeDisassembler : IShaderDisassembler
    {
        public int Calls { get; private set; }
        public string Identity { get; set; } = "test-assembler-v1";
        public string CacheIdentity => Identity;
        public string Disassemble(byte[] bytecode)
        {
            Calls++;
            return "ps_3_0\nmov oC0, c0\n";
        }
    }

    private sealed class CacheFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ClientShaderToolsTests-" + Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(Root, "client shaders");
        public string Output => Path.Combine(Root, "cached shaders");
        public FakeDisassembler Disassembler { get; } = new();
        public CacheFixture() => Directory.CreateDirectory(Input);
        public string Source(string relative, byte[] bytes)
        {
            var path = Path.Combine(Input, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        public ShaderCacheReport Run(string profile = "SM3", bool force = false, bool verifyHashes = false) =>
            new ShaderCache(Disassembler).Run(Input, Output, profile, force, verifyHashes);
        public ShaderManifest Manifest() => JsonSerializer.Deserialize<ShaderManifest>(File.ReadAllText(Path.Combine(Output, "manifest.json")))!;
        public void Dispose()
        {
            var allowed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test fixture cleanup path.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
