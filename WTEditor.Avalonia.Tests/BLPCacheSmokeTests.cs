using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class BLPCacheSmokeTests
{
    [TestMethod]
    public void CascFileReaderUsesLocalPayloadWhenInstalled()
    {
        var secondaryLocalReaderWasCalled = false;

        var bytes = CascFileReader.ReadFile(
            189587,
            _ => [1, 2, 3],
            _ =>
            {
                secondaryLocalReaderWasCalled = true;
                return new CascFileReader.LocalCascReadResult(true, [4, 5, 6], string.Empty);
            });

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bytes);
        Assert.IsFalse(secondaryLocalReaderWasCalled);
    }

    [TestMethod]
    public void CascFileReaderUsesSecondaryLocalIndexReader()
    {
        var bytes = CascFileReader.ReadFile(
            189587,
            _ => throw new FileNotFoundException("not installed"),
            _ => new CascFileReader.LocalCascReadResult(true, "BLP2"u8.ToArray(), string.Empty));

        CollectionAssert.AreEqual("BLP2"u8.ToArray(), bytes);
    }

    [TestMethod]
    public void CascFileReaderReportsPayloadMissingWithoutOnlineFallback()
    {
        var exception = Assert.ThrowsException<FileNotFoundException>(() =>
            CascFileReader.ReadFile(
                189587,
                _ => throw new FileNotFoundException("native error 2"),
                _ => new CascFileReader.LocalCascReadResult(
                    false,
                    [],
                    "the payload is not present in the installed local CASC archives")));

        StringAssert.Contains(exception.Message, "not present in the installed local CASC archives");
        StringAssert.Contains(exception.Message, "No online fallback was attempted");
    }

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
