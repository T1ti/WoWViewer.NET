namespace WTEditor.Application.Models;

public sealed record ClientBuildProfile(string Product, bool IsClassic)
{
    public static ClientBuildProfile From(ClientConfiguration configuration)
    {
        var product = configuration.WowProduct.Trim();
        return new ClientBuildProfile(
            product,
            product.StartsWith("wow_classic", StringComparison.OrdinalIgnoreCase));
    }

    public bool CanEditScale(EditorObjectSnapshot selection) =>
        !(IsClassic && selection.Data is WorldModelObjectData);
}
