using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Cache;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class BLPCacheSmokeTests
{
    [DataTestMethod]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    public void DecodedTextureIsDiscardedWhenItIsStale(
        bool hasUsers,
        bool hasCachedTexture,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            BLPCache.ShouldDiscardDecodedTexture(hasUsers, hasCachedTexture));
    }

    [DataTestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void ParsedTerrainIsDiscardedOnlyWhenItsTileHasNoUsers(
        bool hasUsers,
        bool expected)
    {
        Assert.AreEqual(expected, ADTCache.ShouldDiscardParsedTile(hasUsers));
    }
}
