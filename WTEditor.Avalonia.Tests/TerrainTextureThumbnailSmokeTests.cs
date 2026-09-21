using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class TerrainTextureThumbnailSmokeTests
{
    [TestMethod]
    public async Task DisposedServiceRejectsNewLoads()
    {
        var service = new TerrainTextureThumbnailService();
        service.Dispose();

        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(
            () => service.LoadAsync(1)).ConfigureAwait(false);
    }
}
