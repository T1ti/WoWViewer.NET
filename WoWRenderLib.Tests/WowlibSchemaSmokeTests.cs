using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWLib;
using WoWLib.Database;
using Versions = WoWLib.Versions;
using WoWRenderLib.Database;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class WowlibSchemaSmokeTests
{
    [DataTestMethod]
    [DataRow(Expansion.Vanilla)]
    [DataRow(Expansion.Wotlk)]
    [DataRow(Expansion.Shadowlands)]
    [DataRow(Expansion.Dragonflight)]
    public void MapColumnsResolveByWoWDBDefsNames(Expansion expansion)
    {
        using var version = expansion switch
        {
            Expansion.Vanilla => Versions.Global.Vanilla,
            Expansion.Wotlk => Versions.Global.Wotlk,
            Expansion.Shadowlands => Versions.Global.Shadowlands,
            Expansion.Dragonflight => Versions.Global.Dragonflight,
            _ => throw new ArgumentOutOfRangeException(nameof(expansion))
        };
        using var table = Table.Open("Map", version);
        var row = table.AppendRow();
        var directory = Db2Schema.RequireColumn(table, "Map", "Directory");
        var mapName = Db2Schema.RequireColumn(table, "Map", "MapName_lang");
        table.SetString(row, directory, "Northrend", 0);
        table.SetString(row, mapName, "Northrend display name", 0);

        Assert.AreEqual("Northrend", table.GetString(row, directory, 0));
        Assert.AreEqual("Northrend display name", table.GetString(row, mapName, 0));
        using var column = table.ColumnInfo(mapName);
        Assert.AreEqual("MapName_lang", column.DbdName);
    }

    [DataTestMethod]
    [DataRow("LiquidType", "SoundBank")]
    [DataRow("LiquidType", "MaterialID")]
    [DataRow("LiquidType", "FrameCountTexture")]
    [DataRow("LiquidMaterial", "LVF")]
    [DataRow("LiquidObject", "LiquidTypeID")]
    [DataRow("LiquidTypeXTexture", "FileDataID")]
    [DataRow("WMOMinimapTexture", "WMOID")]
    [DataRow("LightData", "LightParamID")]
    [DataRow("LightData", "DirectColor")]
    [DataRow("LightParams", "WaterShallowAlpha")]
    public void RendererSchemaMappingsUseUpstreamNames(string tableName, string columnName)
    {
        using var table = Table.Open(tableName, Versions.Global.Shadowlands);
        var index = Db2Schema.RequireColumn(table, tableName, columnName);
        using var column = table.ColumnInfo(index);
        Assert.AreEqual(columnName, column.DbdName);
    }
}
