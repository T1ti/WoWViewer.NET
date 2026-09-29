using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoWViewer.ClientShaderTools;

public sealed record CachedPermutation(int Ordinal, int RecordOffset, int PayloadOffset,
    uint Metadata0, uint Metadata1, ushort Metadata2, ushort Metadata3, int ByteCount,
    string BytecodeHash, string ShaderProfile, string BinaryPath, string AssemblyPath,
    string SummaryPath, string AssemblyHash, string Status, string? Error = null);
public sealed record CachedShaderFile(string ClientPath, long Length, long LastWriteUtcTicks,
    string SourceHash, string Status, CachedPermutation[] Permutations,
    string[] EffectNames, string? Error = null);
public sealed record ShaderManifest(int SchemaVersion, string SourceRoot,
    string Disassembler, CachedShaderFile[] Files);
public sealed record ShaderCacheReport(int Files, int Processed, int Reused, int Permutations,
    int UniquePrograms, int Disassembled, int EmptyFiles, IReadOnlyList<string> Errors);

public sealed class ShaderCache(IShaderDisassembler disassembler)
{
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ShaderCacheReport Run(string shaderRoot, string outputDirectory, string profile = "SM3",
        bool force = false, bool verifyHashes = false)
    {
        if (profile is not ("SM3" or "AllDx9"))
            throw new ArgumentException("Profile must be SM3 or AllDx9.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(shaderRoot));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Shader root not found: {root}");
        if (Inside(output, root) || Inside(root, output))
            throw new ArgumentException("Source and cache directories must not overlap.");
        if (!Regex.IsMatch(disassembler.CacheIdentity, @"\A[a-zA-Z0-9_-]+\z"))
            throw new ArgumentException("Disassembler cache identity must be a safe directory name.");

        var previous = LoadManifest(output, force);
        var reusable = previous is { SchemaVersion: SchemaVersion } &&
            string.Equals(previous.SourceRoot, root, PathComparison) &&
            previous.Disassembler == disassembler.CacheIdentity;
        var oldFiles = reusable ? previous!.Files.ToDictionary(f => f.ClientPath, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CachedShaderFile>(StringComparer.OrdinalIgnoreCase);
        var oldObjects = oldFiles.Values.SelectMany(f => f.Permutations).Where(p => p.ByteCount > 0)
            .GroupBy(p => p.BytecodeHash).ToDictionary(g => g.Key, g => g.First());
        var files = new List<CachedShaderFile>();
        var errors = new List<string>();
        var objects = new Dictionary<string, CachedPermutation>();
        var processed = 0;
        var reused = 0;
        var disassembled = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => Selected(Path.GetRelativePath(root, p).Replace('\\', '/'), profile))
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var clientPath = "shaders/" + relative;
            var info = new FileInfo(path);
            oldFiles.TryGetValue(clientPath, out var old);
            byte[]? bytes = null;
            var hash = "";
            var canReuse = !force && old is not null && old.Length == info.Length &&
                old.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks;
            if (!canReuse || verifyHashes)
            {
                bytes = File.ReadAllBytes(path);
                hash = Hash(bytes);
                canReuse = !force && old is not null && old.SourceHash == hash;
            }

            CachedShaderFile entry;
            if (canReuse)
            {
                reused++;
                entry = old! with { Length = info.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks };
            }
            else
            {
                processed++;
                try
                {
                    if (relative.EndsWith(".wfx", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = Encoding.UTF8.GetString(bytes!);
                        WriteText(output, "effects/" + Path.GetFileName(path), text);
                        entry = new(clientPath, info.Length, info.LastWriteTimeUtc.Ticks, hash,
                            "effect-description", [], Regex.Matches(text, @"\bEffect\s*\(\s*([^\)]+)\)")
                                .Select(m => m.Groups[1].Value.Trim()).ToArray());
                    }
                    else
                    {
                        var permutations = BlsParser.Parse(bytes!).Select(p =>
                        {
                            var payloadHash = Hash(p.Bytecode);
                            return new CachedPermutation(p.Ordinal, p.RecordOffset, p.PayloadOffset,
                                p.Metadata0, p.Metadata1, p.Metadata2, p.Metadata3, p.Bytecode.Length,
                                payloadHash, ShaderProfile(p.Bytecode), "", "", "", "",
                                p.Bytecode.Length == 0 ? "empty-permutation" : "extracted");
                        }).ToArray();
                        entry = new(clientPath, info.Length, info.LastWriteTimeUtc.Ticks, hash,
                            info.Length == 0 ? "empty-file" : "parsed", permutations, []);
                    }
                }
                catch (InvalidDataException ex)
                {
                    entry = new(clientPath, info.Length, info.LastWriteTimeUtc.Ticks, hash,
                        "error", [], [], ex.Message);
                }
            }

            if (entry.Error is not null)
                errors.Add($"{clientPath}: {entry.Error}");
            if (entry.Status == "effect-description" && !File.Exists(Artifact(output, "effects/" + Path.GetFileName(path))))
                WriteText(output, "effects/" + Path.GetFileName(path), Encoding.UTF8.GetString(bytes ?? File.ReadAllBytes(path)));
            var ready = new List<CachedPermutation>();
            foreach (var record in entry.Permutations)
            {
                if (record.ByteCount == 0)
                {
                    ready.Add(record);
                    continue;
                }
                if (!Regex.IsMatch(record.BytecodeHash, @"\A[a-f0-9]{64}\z"))
                    throw new InvalidDataException("Invalid bytecode hash in cache manifest; use --force to rebuild it.");
                if (objects.TryGetValue(record.BytecodeHash, out var shared))
                {
                    ready.Add(CopyOutputs(record, shared));
                    if (shared.Error is not null)
                        errors.Add($"{clientPath} permutation {record.Ordinal}: {shared.Error}");
                    continue;
                }
                var stem = $"objects/{disassembler.CacheIdentity}/{record.BytecodeHash}";
                var updated = (oldObjects.TryGetValue(record.BytecodeHash, out var oldObject)
                    ? CopyOutputs(record, oldObject) : record) with
                { BinaryPath = stem + ".bin", AssemblyPath = stem + ".asm", SummaryPath = stem + ".json" };
                try
                {
                    var binary = Artifact(output, updated.BinaryPath);
                    byte[] Payload()
                    {
                        if (bytes is not null)
                            return bytes.AsSpan(record.PayloadOffset, record.ByteCount).ToArray();
                        using var source = File.OpenRead(path);
                        if (record.PayloadOffset < 0 || record.ByteCount > source.Length - record.PayloadOffset)
                            throw new InvalidDataException("Cached payload offsets are out of bounds; use --force.");
                        source.Position = record.PayloadOffset;
                        var payload = new byte[record.ByteCount];
                        source.ReadExactly(payload);
                        return payload;
                    }
                    if (!File.Exists(binary) || force || verifyHashes && Hash(File.ReadAllBytes(binary)) != record.BytecodeHash)
                    {
                        var payload = Payload();
                        if (Hash(payload) != record.BytecodeHash)
                            throw new InvalidDataException("Source payload no longer matches the cached hash; use --force.");
                        WriteBytes(output, updated.BinaryPath, payload);
                    }
                    var assemblyPath = Artifact(output, updated.AssemblyPath);
                    var needAssembly = force || !File.Exists(assemblyPath) ||
                        verifyHashes && Hash(File.ReadAllBytes(assemblyPath)) != updated.AssemblyHash;
                    if (updated.Error is not null && !force)
                        throw new InvalidDataException(updated.Error);
                    if (needAssembly)
                    {
                        var assembly = disassembler.Disassemble(File.ReadAllBytes(binary));
                        disassembled++;
                        WriteText(output, updated.AssemblyPath, assembly);
                        updated = updated with { AssemblyHash = Hash(Encoding.UTF8.GetBytes(assembly)) };
                    }
                    if (needAssembly || !File.Exists(Artifact(output, updated.SummaryPath)))
                    {
                        var assembly = File.ReadAllText(assemblyPath);
                        var lines = assembly.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).ToArray();
                        var registers = Regex.Matches(string.Join('\n', lines), @"\b(?:(?:c|i|b|s|v|r|a|oC|oT|oD|o)\d+|oPos|oFog|vPos|vFace|aL)\b")
                            .Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToArray();
                        var opcodes = lines.Select(l => Regex.Match(l, @"^\s*([a-z][a-z0-9_]*)\b"))
                            .Where(m => m.Success).Select(m => m.Groups[1].Value)
                            .GroupBy(x => x).OrderBy(g => g.Key, StringComparer.Ordinal)
                            .ToDictionary(g => g.Key, g => g.Count());
                        WriteText(output, updated.SummaryPath, JsonSerializer.Serialize(new
                        {
                            updated.BytecodeHash, updated.ShaderProfile, updated.ByteCount,
                            Registers = registers, OpcodeCounts = opcodes,
                            Note = "DX9 assembly and register inventory; original HLSL and selector meanings are not recovered."
                        }, JsonOptions));
                    }
                    updated = updated with { Status = "disassembled", Error = null };
                }
                catch (Exception ex) when (ex is InvalidDataException or ExternalException or DllNotFoundException or
                    EntryPointNotFoundException or PlatformNotSupportedException)
                {
                    updated = updated with { Status = "error", Error = ex.Message };
                    errors.Add($"{clientPath} permutation {record.Ordinal}: {ex.Message}");
                }
                objects[record.BytecodeHash] = updated;
                ready.Add(updated);
            }
            entry = entry with { Permutations = ready.ToArray() };
            files.Add(entry);
        }
        if (files.Count == 0)
            throw new InvalidDataException("No matching DX9 BLS or WFX files found; point --shader-root at the shaders directory.");

        var selectedCount = files.Count;
        var selectedPermutations = files.Sum(f => f.Permutations.Length);
        var selectedEmptyFiles = files.Count(f => f.Status == "empty-file");
        // Switching to SM3 must not discard the cached fallback-profile records.
        // Removed sources disappear from the manifest; content-addressed objects
        // remain available and are never automatically deleted.
        foreach (var old in oldFiles.Values.Where(f => f.ClientPath.StartsWith("shaders/", StringComparison.Ordinal) &&
            !Selected(f.ClientPath[8..], profile)))
        {
            var path = Path.GetFullPath(Path.Combine(root, old.ClientPath[8..]));
            if (Inside(path, root) && File.Exists(path))
                files.Add(old);
        }
        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.ClientPath, right.ClientPath));
        var manifest = new ShaderManifest(SchemaVersion, root, disassembler.CacheIdentity, files.ToArray());
        WriteText(output, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
        var index = new StringBuilder("# Cached 3.3.5 DX9 shaders\n\nRead these saved outputs before reopening BLS files. " +
            "Assembly is disassembled bytecode, not original HLSL. Metadata fields are raw client values.\n\n" +
            "| Client path | Status | Permutations | Cached program |\n| --- | --- | ---: | --- |\n");
        var csv = new StringBuilder("client_path,ordinal,metadata_0,metadata_1,metadata_2,metadata_3,bytes,bytecode_sha256,shader_profile,binary,assembly,summary,status\n");
        foreach (var file in files)
        {
            var programPath = "programs/" + file.ClientPath[8..] + ".md";
            var program = new StringBuilder($"# {file.ClientPath}\n\nSource SHA-256: `{file.SourceHash}`. " +
                $"Status: `{file.Status}`. Container bytes: {file.Length}.\n\n");
            if (file.Error is not null)
                program.AppendLine(file.Error);
            program.Append("| Ordinal | Metadata 0 / 1 / 2 / 3 | Bytes | Actual profile | DX9 assembly |\n| ---: | --- | ---: | --- | --- |\n");
            foreach (var record in file.Permutations)
            {
                var link = record.AssemblyPath.Length > 0 ? Path.GetRelativePath(
                    Path.GetDirectoryName(Artifact(output, programPath))!, Artifact(output, record.AssemblyPath)).Replace('\\', '/') : "";
                program.AppendLine($"| {record.Ordinal} | {record.Metadata0} / {record.Metadata1} / {record.Metadata2} / {record.Metadata3} | " +
                    $"{record.ByteCount} | {record.ShaderProfile} | " + (link.Length > 0 ? $"[{record.Status}]({link}) |" : $"{record.Status} |"));
                csv.AppendLine(string.Join(',', new[] { file.ClientPath, record.Ordinal.ToString(), record.Metadata0.ToString(),
                    record.Metadata1.ToString(), record.Metadata2.ToString(), record.Metadata3.ToString(), record.ByteCount.ToString(),
                    record.BytecodeHash, record.ShaderProfile, record.BinaryPath, record.AssemblyPath, record.SummaryPath, record.Status }.Select(Csv)));
            }
            if (file.EffectNames.Length > 0)
                program.AppendLine("\nEffects: " + string.Join(", ", file.EffectNames.Select(x => $"`{x}`")));
            WriteText(output, programPath, program.ToString());
            index.AppendLine($"| `{file.ClientPath}` | {file.Status} | {file.Permutations.Length} | [program]({programPath}) |");
        }
        WriteText(output, "index.md", index.ToString());
        WriteText(output, "permutations.csv", csv.ToString());
        return new(selectedCount, processed, reused, selectedPermutations, objects.Count,
            disassembled, selectedEmptyFiles, errors);
    }

    private static CachedPermutation CopyOutputs(CachedPermutation destination, CachedPermutation source) => destination with
    {
        BinaryPath = source.BinaryPath, AssemblyPath = source.AssemblyPath, SummaryPath = source.SummaryPath,
        AssemblyHash = source.AssemblyHash, Status = source.Status, Error = source.Error
    };
    private static ShaderManifest? LoadManifest(string output, bool force)
    {
        var path = Path.Combine(output, "manifest.json");
        if (!File.Exists(path))
            return null;
        try
        {
            // Force refreshes the selected scope, but must retain compatible
            // fallback records so a later scope switch can still reuse them.
            return JsonSerializer.Deserialize<ShaderManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException) when (force)
        {
            return null;
        }
    }
    private static bool Selected(string path, string scope)
    {
        var parts = path.Split('/');
        if (parts.Length == 2 && parts[0].Equals("effects", StringComparison.OrdinalIgnoreCase))
            return path.EndsWith(".wfx", StringComparison.OrdinalIgnoreCase);
        if (parts.Length != 3 || !path.EndsWith(".bls", StringComparison.OrdinalIgnoreCase))
            return false;
        return scope == "SM3"
            ? parts[0].Equals("vertex", StringComparison.OrdinalIgnoreCase) && parts[1] == "vs_3_0" ||
                parts[0].Equals("pixel", StringComparison.OrdinalIgnoreCase) && parts[1] == "ps_3_0"
            : parts[0].Equals("vertex", StringComparison.OrdinalIgnoreCase) && parts[1] is "vs_1_1" or "vs_2_0" or "vs_3_0" ||
                parts[0].Equals("pixel", StringComparison.OrdinalIgnoreCase) && parts[1] is "ps_1_1" or "ps_1_4" or "ps_2_0" or "ps_3_0";
    }
    private static string ShaderProfile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
            return "unknown";
        var token = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var stage = (token >> 16) switch { 0xFFFE => "vs", 0xFFFF => "ps", _ => "unknown" };
        return stage == "unknown" ? stage : $"{stage}_{(token >> 8) & 255}_{token & 255}";
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool Inside(string path, string parent) => path.Equals(parent, PathComparison) || path.StartsWith(parent + Path.DirectorySeparatorChar, PathComparison);
    private static string Artifact(string output, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(output, relative));
        if (!Inside(path, output) || path.Equals(output, PathComparison))
            throw new InvalidDataException("Cache artifact path escapes the output directory.");
        return path;
    }
    private static void WriteText(string output, string relative, string text) => WriteBytes(output, relative, Encoding.UTF8.GetBytes(text));
    private static void WriteBytes(string output, string relative, byte[] bytes)
    {
        var path = Artifact(output, relative);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
}
