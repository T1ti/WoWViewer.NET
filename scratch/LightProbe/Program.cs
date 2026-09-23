using DBCD.IO;
using DBCD.IO.Attributes;
using WoWLib;
using Fs = WoWLib.Filesystem;

const string clientPath = @"C:\Users\Titi\Games\WoW-Classic-Forever";
var listfile = @"C:\Users\Titi\Dev\WoWViewer.NET\WTEditor.Avalonia\bin\Debug\net10.0\community-listfile.csv";
var version = new ClientVersion(1, 60, 1, 69893, ClientFlavor.ClassicEra);
var project = Path.Combine(Path.GetTempPath(), "WoWViewer.NET", "light-probe");
Directory.CreateDirectory(project);
using var settings = new Fs.FileSystemSettings(clientPath, version, Locale.enUS, project, listfile, new FileDataId(), "wow_classic_beta");
using var fs = Fs.FileSystem.Open(settings);

var lightParamsBytes = fs.ReadFile(new FileDataId(1334669));
var lightParamsParser = new DBParser(new MemoryStream(lightParamsBytes, writable: false));
Console.WriteLine($"LightParams header id={lightParamsParser.Identifier} rows={lightParamsParser.RecordsCount} fields={lightParamsParser.FieldsCount} size={lightParamsParser.RecordSize} layout=0x{lightParamsParser.LayoutHash:X8} idField={lightParamsParser.IdFieldIndex} flags={lightParamsParser.Flags}");
var lightParamsRows = new Dictionary<int, NonInlineLightParams>();
lightParamsParser.PopulateRecords(lightParamsRows);
var lightParams12 = lightParamsRows[12];
Console.WriteLine($"LightParams 12: id={lightParams12.Id} water={lightParams12.WaterShallowAlpha}/{lightParams12.WaterDeepAlpha} ocean={lightParams12.OceanShallowAlpha}/{lightParams12.OceanDeepAlpha}");

var productionLighting = WoWRenderLib.Loaders.WorldLightingDataLoader.LoadTemporaryDefault(fs)!;
Console.WriteLine($"Production lighting: colors={productionLighting.HasLiquidColorData} alphas={productionLighting.HasLiquidAlphaData} water={productionLighting.WaterShallowAlpha}/{productionLighting.WaterDeepAlpha} ocean={productionLighting.OceanShallowAlpha}/{productionLighting.OceanDeepAlpha}");
Console.WriteLine($"Production colors: ocean={productionLighting.OceanCloseColor}->{productionLighting.OceanFarColor} river={productionLighting.RiverCloseColor}->{productionLighting.RiverFarColor} ambient={productionLighting.AmbientColor} direct={productionLighting.DirectColor}");
WoWRenderLib.Structs.WorldLiquidMaterialCatalog.Shared.Configure(fs);
using (var typeSchema = version.FormatLineage)
using (var typeTable = WoWLib.Database.Table.Open("LiquidType", typeSchema))
using (var typeKey = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LiquidType.db2")))
using (var typeIdKey = new WoWLib.FileKey(new WoWLib.FileDataId(typeKey.Fdid!.Value)))
{
    typeTable.Read(fs, typeIdKey);
    var idColumn = typeTable.ColumnIndex("id");
    var ids = Enumerable.Range(0, checked((int)typeTable.RowCount))
        .Select(row => typeTable.GetInt((ulong)row, idColumn, 0))
        .ToArray();
    Console.WriteLine($"LiquidType IDs: {string.Join(',', ids)}; has1250={ids.Contains(1250)}");
    if (ids.Contains(1250))
    {
        var row = typeTable.FindById(1250);
        Console.WriteLine($"LiquidType 1250 row={row}");
        for (var column = 0UL; column < typeTable.ColumnCount; column++)
        {
            var info = typeTable.ColumnInfo(column);
            try { Console.WriteLine($"LiquidType 1250 {info.Name}={typeTable.GetInt(row, column, 0)}"); }
            catch { }
        }
    }
}
var material1250 = WoWRenderLib.Structs.WorldLiquidMaterialCatalog.Shared.Resolve(1250, 42);
Console.WriteLine($"Material 1250/42: family={material1250.Family} water={material1250.WaterType} textures={material1250.TextureFileDataIds.Length}");
using (var textureSchema = version.FormatLineage)
using (var textureTable = WoWLib.Database.Table.Open("LiquidTypeXTexture", textureSchema))
using (var textureKey = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LiquidTypeXTexture.db2")))
using (var textureIdKey = new WoWLib.FileKey(new WoWLib.FileDataId(textureKey.Fdid!.Value)))
{
    textureTable.Read(fs, textureIdKey);
    for (var row = 0UL; row < textureTable.RowCount; row++)
    {
        if (textureTable.GetInt(row, textureTable.ColumnIndex("liquid_type_id"), 0) != 1250)
            continue;
        Console.WriteLine($"LiquidTypeXTexture 1250: fdid={textureTable.GetInt(row, textureTable.ColumnIndex("file_data_id"), 0)} type={textureTable.GetInt(row, textureTable.ColumnIndex("type"), 0)} order={textureTable.GetInt(row, textureTable.ColumnIndex("order_index"), 0)}");
    }
}
for (ushort liquidTypeId = 1; liquidTypeId <= 51; liquidTypeId++)
{
    var material = WoWRenderLib.Structs.WorldLiquidMaterialCatalog.Shared.Resolve(liquidTypeId, 0);
    Console.WriteLine($"Material {liquidTypeId}: family={material.Family} water={material.WaterType} textures={material.TextureFileDataIds.Length}");
}
using (var objectSchema = version.FormatLineage)
using (var objectTable = WoWLib.Database.Table.Open("LiquidObject", objectSchema))
using (var objectKey = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LiquidObject.db2")))
using (var objectIdKey = new WoWLib.FileKey(new WoWLib.FileDataId(objectKey.Fdid!.Value)))
{
    objectTable.Read(fs, objectIdKey);
    Console.WriteLine($"LiquidObject rows={objectTable.RowCount} columns={objectTable.ColumnCount}");
    for (var column = 0UL; column < objectTable.ColumnCount; column++)
        Console.WriteLine($"LiquidObject column {column}: {objectTable.ColumnInfo(column).Name}");
    var objectIdColumn = objectTable.ColumnIndex("id");
    var objectIds = Enumerable.Range(0, checked((int)objectTable.RowCount))
        .Select(row => objectTable.GetInt((ulong)row, objectIdColumn, 0))
        .ToArray();
    Console.WriteLine($"LiquidObject id range={objectIds.Min()}..{objectIds.Max()}, first={string.Join(',', objectIds.Take(20))}, has42={objectIds.Contains(42)}, has1250={objectIds.Contains(1250)}");
    foreach (var ordinal in new ulong[] { 41, 42, 1249, 1250 })
    {
        Console.WriteLine(
            $"LiquidObject ordinal {ordinal}: id={objectTable.GetInt(ordinal, objectIdColumn, 0)}, " +
            $"type={objectTable.GetInt(ordinal, objectTable.ColumnIndex("liquid_type_id"), 0)}, " +
            $"direction={objectTable.GetFloat(ordinal, objectTable.ColumnIndex("flow_direction"), 0)}, " +
            $"speed={objectTable.GetFloat(ordinal, objectTable.ColumnIndex("flow_speed"), 0)}");
    }
    foreach (var wantedObjectId in new uint[] { 42, 1250 })
    {
        try
        {
            var row = objectTable.FindById(wantedObjectId);
            Console.WriteLine($"LiquidObject {wantedObjectId} row={row}, type={objectTable.GetInt(row, objectTable.ColumnIndex("liquid_type_id"), 0)}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"LiquidObject {wantedObjectId} failure: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
return;

using (var schema = version.FormatLineage)
using (var table = WoWLib.Database.Table.Open("LightParams", schema))
using (var key = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LightParams.db2")))
using (var idKey = new WoWLib.FileKey(new WoWLib.FileDataId(key.Fdid!.Value)))
{
    table.Read(fs, idKey);
    Console.WriteLine($"LightParams WowLib rows={table.RowCount} cols={table.ColumnCount}");
    for (var c = 0UL; c < table.ColumnCount; c++)
    {
        var ci = table.ColumnInfo(c);
        Console.WriteLine($"LightParams column {c}: {ci.Name} type={ci.Type} array={ci.ArrayLen}");
    }
    var row = table.FindById(12);
    foreach (var name in new[] { "water_shallow_alpha", "water_deep_alpha", "ocean_shallow_alpha", "ocean_deep_alpha" })
    {
        var column = table.ColumnIndex(name);
        Console.WriteLine($"LightParams WowLib 12 {name}={table.GetFloat(row, column, 0)}");
    }
}

var modernPath = Path.Combine(Path.GetTempPath(), "LightData-1.60.1.69876.db2");
if (File.Exists(modernPath))
{
    var modernBytes = File.ReadAllBytes(modernPath);
    var modernParser = new DBParser(new MemoryStream(modernBytes, writable: false));
    Console.WriteLine($"ModernLightData fields={typeof(ModernLightData).GetFields().Length}");
    Console.WriteLine($"modern header id={modernParser.Identifier} rows={modernParser.RecordsCount} fields={modernParser.FieldsCount} size={modernParser.RecordSize} layout=0x{modernParser.LayoutHash:X8}");
    var modernRows = new Dictionary<int, ModernLightData>();
    modernParser.PopulateRecords(modernRows);
    Console.WriteLine($"modern parser rows={modernRows.Count}");
    Console.WriteLine($"modern lp values={string.Join(',', modernRows.Values.Select(x => x.LightParamId).Distinct().OrderBy(x => x))}");
    foreach (var x in modernRows.Values.Where(x => x.LightParamId == 12).Take(10)) Console.WriteLine($"lp12 row id={x.Id} time={x.Time} direct={x.DirectColor} ambient={x.AmbientColor}");
    foreach (var x in modernRows.Values.Take(5)) Console.WriteLine($"modern first id={x.Id} lp={x.LightParamId} time={x.Time} direct={x.DirectColor} ambient={x.AmbientColor}");
    var modern = modernRows.Values.FirstOrDefault(x => x.LightParamId == 12 && x.Time == 1440);
    Console.WriteLine(modern == null ? "modern profile missing" : $"modern profile id={modern.Id} lp={modern.LightParamId} time={modern.Time} direct=0x{modern.DirectColor:X8} ambient=0x{modern.AmbientColor:X8} fog={modern.FogEnd} extra={modern.Field100000000000}");

    var twentyRows = new Dictionary<int, TwentyLightData>();
    var twentyParser = new DBParser(new MemoryStream(modernBytes, writable: false));
    twentyParser.PopulateRecords(twentyRows);
    var twenty = twentyRows.Values.FirstOrDefault(x => unchecked((ushort)x.LightParamId) == 12 && unchecked((ushort)x.Time) == 1440);
    Console.WriteLine(twenty == null ? "twenty profile missing" : $"twenty profile id={twenty.Id} lp={unchecked((ushort)twenty.LightParamId)} time={unchecked((ushort)twenty.Time)} direct=0x{twenty.DirectColor:X8} oceanClose=0x{twenty.OceanCloseColor:X8} oceanFar=0x{twenty.OceanFarColor:X8} riverClose=0x{twenty.RiverCloseColor:X8} riverFar=0x{twenty.RiverFarColor:X8}");

    var requiredRows = new Dictionary<int, RequiredLightData>();
    var requiredParser = new DBParser(new MemoryStream(modernBytes, writable: false));
    requiredParser.PopulateRecords(requiredRows);
    var required = requiredRows.Values.FirstOrDefault(x => unchecked((ushort)x.LightParamId) == 12 && unchecked((ushort)x.Time) == 1440);
    Console.WriteLine(required == null ? "required profile missing" : $"required profile id={required.Id} lp={unchecked((ushort)required.LightParamId)} time={unchecked((ushort)required.Time)} direct=0x{required.DirectColor:X8} ambient=0x{required.AmbientColor:X8}");
}

using (var schema = version.FormatLineage)
using (var table = WoWLib.Database.Table.Open("LightData", schema))
using (var key = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LightData.db2")))
using (var idKey = new WoWLib.FileKey(new WoWLib.FileDataId(key.Fdid!.Value)))
{
    table.Read(fs, idKey);
    Console.WriteLine($"Table.Open succeeded rows={table.RowCount} cols={table.ColumnCount}");
    for (var c = 0UL; c < table.ColumnCount; c++)
    {
        var ci = table.ColumnInfo(c);
        Console.WriteLine($"column {c}: {ci.Name} type={ci.Type} array={ci.ArrayLen}");
    }
}


foreach (var id in new uint[] { 1375580, 1371380 })
{
    var bytes = fs.ReadFile(new FileDataId(id));
    Console.WriteLine($"fdid {id} len={bytes.Length} magic={BitConverter.ToString(bytes,0,4)}");
    if (id == 1375580)
    {
        var parser = new DBParser(new MemoryStream(bytes, writable: false));
        var rows = new Dictionary<int, OldLightData>();
        parser.PopulateRecords(rows);
        Console.WriteLine($"DBCD parser rows={rows.Count} fields={parser.FieldsCount} size={parser.RecordSize} id={parser.IdFieldIndex}");
        if (rows.Count > 0)
        {
            var first = rows.First().Value;
            Console.WriteLine($"first: id={first.Id} lp={first.LightParamId} time={first.Time} direct=0x{first.DirectColor:X} ambient=0x{first.AmbientColor:X} fog={first.FogEnd}");
        }
    }
}

using (var schema = version.FormatLineage)
using (var table = WoWLib.Database.Table.Open("LiquidType", schema))
using (var key = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LiquidType.db2")))
using (var idKey = new WoWLib.FileKey(new WoWLib.FileDataId(key.Fdid!.Value)))
{
    table.Read(fs, idKey);
    Console.WriteLine($"LiquidType table rows={table.RowCount} cols={table.ColumnCount}");
    for (var c = 0UL; c < table.ColumnCount; c++)
    {
        var ci = table.ColumnInfo(c);
        Console.WriteLine($"LiquidType column {c}: {ci.Name} type={ci.Type} array={ci.ArrayLen}");
    }
    var textureColumn = table.ColumnIndex("texture");
    var idColumn = table.ColumnIndex("id");
    foreach (var wanted in new[] { 1UL, 2UL, 3UL, 14UL })
    {
        try
        {
            var row = table.FindById((uint)wanted);
            var values = Enumerable.Range(0, 6).Select(i => table.GetString(row, textureColumn, (ulong)i)).ToArray();
            Console.WriteLine($"LiquidType {wanted}: {string.Join(" | ", values)}");
        }
        catch (Exception ex) { Console.WriteLine($"LiquidType {wanted}: {ex.GetType().Name} {ex.Message}"); }
    }
}

using (var schema = version.FormatLineage)
using (var table = WoWLib.Database.Table.Open("LiquidTypeXTexture", schema))
using (var key = fs.Resolve(new WoWLib.FileKey("DBFilesClient/LiquidTypeXTexture.db2")))
using (var idKey = new WoWLib.FileKey(new WoWLib.FileDataId(key.Fdid!.Value)))
{
    table.Read(fs, idKey);
    Console.WriteLine($"LiquidTypeXTexture table rows={table.RowCount} cols={table.ColumnCount}");
    for (var c = 0UL; c < table.ColumnCount; c++)
    {
        var ci = table.ColumnInfo(c);
        Console.WriteLine($"LiquidTypeXTexture column {c}: {ci.Name} type={ci.Type} array={ci.ArrayLen}");
    }
    var liquidTypeColumn = table.ColumnIndex("liquid_type_id");
    var fileColumn = table.ColumnIndex("file_data_id");
    var orderColumn = table.ColumnIndex("order_index");
    var typeColumn = table.ColumnIndex("type");
    for (var row = 0UL; row < table.RowCount; row++)
    {
        var liquidType = table.GetInt(row, liquidTypeColumn, 0);
        if (liquidType is not (1 or 2 or 14))
            continue;
        Console.WriteLine($"LTXT row={row} liquid={liquidType} fdid={table.GetInt(row, fileColumn, 0)} order={table.GetInt(row, orderColumn, 0)} type={table.GetInt(row, typeColumn, 0)}");
    }
}

public sealed class OldLightData
{
    public int Id;
    public int LightParamId;
    public int Time;
    public int DirectColor;
    public int AmbientColor;
    public int SkyTopColor;
    public int SkyMiddleColor;
    public int SkyBand1Color;
    public int SkyBand2Color;
    public int SkySmogColor;
    public int SkyFogColor;
    public int SunColor;
    public int CloudSunColor;
    public int CloudEmissiveColor;
    public int CloudLayer1AmbientColor;
    public int CloudLayer2AmbientColor;
    public int OceanCloseColor;
    public int OceanFarColor;
    public int RiverCloseColor;
    public int RiverFarColor;
    public int ShadowOpacity;
    public float FogEnd;
    public float FogScaler;
    public float FogDensity;
    public float FogHeight;
    public float FogHeightScaler;
    public float FogHeightDensity;
    public float FogZScalar;
    public float MainFogStartDist;
    public float MainFogEndDist;
    public float SunFogAngle;
    public float CloudDensity;
    public int ColorGradingFileDataId;
    public int DarkerColorGradingFileDataId;
    public int HorizonAmbientColor;
    public int GroundAmbientColor;
    public int EndFogColor;
    public float EndFogColorDistance;
    public float FogStartOffset;
    public int SunFogColor;
    public float SunFogStrength;
    public int FogHeightColor;
    public int EndFogHeightColor;
    public int Field100044649042;
    public float[] FogHeightCoefficients = new float[4];
    public float[] MainFogCoefficients = new float[4];
    public float[] HeightDensityFogCoeff = new float[4];
}

public sealed class NonInlineLightParams
{
    [Index(true)]
    public int Id;
    public float[] OverrideCelestialSphere = new float[3];
    public float[] OverrideSunPosition = new float[3];
    public byte HighlightSky;
    public ushort LightSkyboxId;
    public byte CloudTypeId;
    public float Glow;
    public float WaterShallowAlpha;
    public float WaterDeepAlpha;
    public float OceanShallowAlpha;
    public float OceanDeepAlpha;
}

public sealed class ModernLightData
{
    public int Id;
    public int LightParamId;
    public int Time;
    public int DirectColor;
    public int AmbientColor;
    public int SkyTopColor;
    public int SkyMiddleColor;
    public int SkyBand1Color;
    public int SkyBand2Color;
    public int SkySmogColor;
    public int SkyFogColor;
    public int SunColor;
    public int CloudSunColor;
    public int CloudEmissiveColor;
    public int CloudLayer1AmbientColor;
    public int CloudLayer2AmbientColor;
    public int OceanCloseColor;
    public int OceanFarColor;
    public int RiverCloseColor;
    public int RiverFarColor;
    public int ShadowOpacity;
    public float FogEnd;
    public float FogScaler;
    public float FogDensity;
    public float FogHeight;
    public float FogHeightScaler;
    public float FogHeightDensity;
    public float FogZScalar;
    public float MainFogStartDist;
    public float MainFogEndDist;
    public float SunFogAngle;
    public float CloudDensity;
    public int ColorGradingFileDataId;
    public int DarkerColorGradingFileDataId;
    public int HorizonAmbientColor;
    public int GroundAmbientColor;
    public int EndFogColor;
    public float EndFogColorDistance;
    public float FogStartOffset;
    public int SunFogColor;
    public float SunFogStrength;
    public int FogHeightColor;
    public int EndFogHeightColor;
    public int Field100000000000;
    public float Field100000000001;
    public int Field100000000002;
    public int Field100000000003;
    public int Field100000000004;
    public float Field100000000005;
    public float Field100000000006;
    public float Field100000000007;
    public float Field100000000008;
    public float Field100000000009;
    public float Field100000000010;
    public int Field100000000011;
    public int Field100000000012;
    public int Field100000000013;
    public int Field100000000014;
    public float Field100000000015;
    public float Field100000000016;
    public float[] FogHeightCoefficients = new float[4];
    public float[] MainFogCoefficients = new float[4];
    public float[] HeightDensityFogCoeff = new float[4];
}

public sealed class RequiredLightData
{
    public int Id;
    public int LightParamId;
    public int Time;
    public int DirectColor;
    public int AmbientColor;
}

public sealed class TwentyLightData
{
    public int Id;
    public int LightParamId;
    public int Time;
    public int DirectColor;
    public int AmbientColor;
    public int SkyTopColor;
    public int SkyMiddleColor;
    public int SkyBand1Color;
    public int SkyBand2Color;
    public int SkySmogColor;
    public int SkyFogColor;
    public int SunColor;
    public int CloudSunColor;
    public int CloudEmissiveColor;
    public int CloudLayer1AmbientColor;
    public int CloudLayer2AmbientColor;
    public int OceanCloseColor;
    public int OceanFarColor;
    public int RiverCloseColor;
    public int RiverFarColor;
}
