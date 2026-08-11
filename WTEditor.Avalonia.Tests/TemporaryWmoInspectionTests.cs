using Microsoft.VisualStudio.TestTools.UnitTesting;
using TACTSharp;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWRenderLib.Loaders;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TemporaryWmoInspectionTests
{
    [TestMethod]
    public async Task InspectAlteracWmos()
    {
        await CASC.Initialize("wow_classic_era", @"C:\Program Files (x86)\World of Warcraft");
        var provider = new TACTSharpFileProvider();
        provider.InitTACT(CASC.buildInstance);
        FileProvider.SetDefaultBuild(TACTSharpFileProvider.BuildName);
        FileProvider.SetProvider(provider, TACTSharpFileProvider.BuildName);

        var exactId = 513045u;
        var exact = WMOLoader.ParseWMO(exactId);
        var exactLines = new List<string> { $"exact id={exactId} materials={exact.Materials.Length}" };
        foreach (var (m, index) in exact.Materials.Select((m, index) => (m, index)))
        {
            var texInfo = new List<string>();
            foreach (var id in new[] { m.TexFileDataID0, m.TexFileDataID1, m.TexFileDataID2 })
            {
                if (id == 0 || !FileProvider.FileExists(id))
                {
                    texInfo.Add($"{id}:missing");
                    continue;
                }

                using var stream = FileProvider.OpenFile(id);
                using var blp = new BLPSharp.BLPFile(stream);
                texInfo.Add($"{id}:fmt={blp.preferredFormat},alpha={blp.alphaSize},size={blp.width}x{blp.height}");
            }

            exactLines.Add($"mat {index}: shader={m.Shader} pixel={m.PixelShader} blend={m.BlendMode} flags={m.TexFileDataID0},{m.TexFileDataID1},{m.TexFileDataID2} [{string.Join(";", texInfo)}]");
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "wallposttunnel01.inspect.txt"), exactLines);

        var wdt = new WDTReader();
        wdt.LoadWDT(775971);
        Console.WriteLine($"WDT tiles={wdt.wdtfile.tiles.Count} maid={wdt.wdtfile.mphd.flags}");

        var seen = new HashSet<uint>();
        for (byte x = 29; x <= 33; x++)
        for (byte y = 29; y <= 33; y++)
        {
            if (!wdt.wdtfile.tileFiles.TryGetValue((x, y), out var ids) || ids.rootADT == 0)
                continue;

            var adt = new ADTReader();
            try
            {
                adt.LoadADT(wdt.wdtfile, x, y);
            }
            catch (Exception e)
            {
                Console.WriteLine($"ADT {x},{y} failed: {e.Message}");
                continue;
            }

            foreach (var entry in adt.adtfile.objects.worldModels.entries)
            {
                if (!seen.Add(entry.mwidEntry))
                    continue;

                try
                {
                    var wmo = WMOLoader.ParseWMO(entry.mwidEntry);
                    var shaderCounts = wmo.Materials.GroupBy(m => (int)m.PixelShader).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
                    var textures = wmo.Materials.SelectMany(m => new[] { m.TexFileDataID0, m.TexFileDataID1, m.TexFileDataID2, m.TexFileDataID3, m.TexFileDataID4, m.TexFileDataID5, m.TexFileDataID6, m.TexFileDataID7, m.TexFileDataID8 }).Where(id => id != 0).Distinct().Count();
                    Console.WriteLine($"WMO {entry.mwidEntry} at {x},{y} mats={wmo.Materials.Length} shaders=[{string.Join(',', shaderCounts)}] textures={textures}");
                    if (x == 31 && y == 31)
                        foreach (var (m, index) in wmo.Materials.Select((m, index) => (m, index)))
                            Console.WriteLine($"  mat {index}: shader={m.Shader} blend={m.BlendMode} tex={m.TexFileDataID0},{m.TexFileDataID1},{m.TexFileDataID2} exists={FileProvider.FileExists(m.TexFileDataID0)}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"WMO {entry.mwidEntry} parse failed: {e.Message}");
                }
            }
        }
    }
}
