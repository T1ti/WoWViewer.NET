namespace WoWRenderLib.DX11;

using System.Numerics;
using System.Text.Json.Serialization;

public sealed class RendererSettings
{
    public bool UseClientRenderingRules { get; set; } = true;
    // Reference-noon colors from the 3.3.5 client lighting profile. They are
    // stored as linear RGB-style factors and serialized with the other
    // renderer settings so a future settings UI can edit them without a
    // renderer API change.
    private Vector3 _ambientColor = new(104f / 255f, 130f / 255f, 154f / 255f);
    private Vector3 _diffuseColor = new(1f, 136f / 255f, 0f);

    [JsonIgnore]
    public Vector3 AmbientColor { get => _ambientColor; set => _ambientColor = value; }

    [JsonIgnore]
    public Vector3 DiffuseColor { get => _diffuseColor; set => _diffuseColor = value; }

    // Vector3 is field-based in System.Numerics and is intentionally not
    // globally enabled in the editor's JSON options. Keep the persisted form
    // explicit and portable instead of relying on serializer implementation
    // details.
    public float AmbientColorR { get => _ambientColor.X; set => _ambientColor.X = value; }
    public float AmbientColorG { get => _ambientColor.Y; set => _ambientColor.Y = value; }
    public float AmbientColorB { get => _ambientColor.Z; set => _ambientColor.Z = value; }
    public float DiffuseColorR { get => _diffuseColor.X; set => _diffuseColor.X = value; }
    public float DiffuseColorG { get => _diffuseColor.Y; set => _diffuseColor.Y = value; }
    public float DiffuseColorB { get => _diffuseColor.Z; set => _diffuseColor.Z = value; }

    public float TerrainRenderDistance { get; set; } = 20_000f;
    public float ModelRenderDistance { get; set; } = 20_000f;
    public float AnimationRenderDistancePercent { get; set; } = 50f;
    public float ParticleRenderDistancePercent { get; set; } = 20f;
    public float MinimumModelScreenSizePixels { get; set; } = 1f;
    public float TerrainLodTransitionPixels { get; set; } = 32f;
    public int TileLoadingDistance { get; set; } = 4;
    public float MovementSpeed { get; set; } = 150f;
    public float MouseSensitivity { get; set; } = 0.1f;
    // The model viewer can use the configured colors; the world viewport uses evaluated world lighting.
    public bool UseConfiguredLighting { get; set; } = true;

    public bool RenderADT { get; set; } = true;
    public bool RenderLiquid { get; set; } = true;
    public bool RenderWMO { get; set; } = true;
    public bool ShowWmoCollisionMesh { get; set; }
    public bool RenderM2 { get; set; } = true;
    public bool RenderParticles { get; set; } = true;
    public bool RenderFog { get; set; }
    public bool EditorDisableScreenGlow { get; set; }
    public bool DisableScreenGlow { get; set; }
    // Client EffectsPanelOptions defaults to the documented Ultra values.
    public int ClientRenderingQuality { get; set; } = 6;
    public int TerrainMip { get; set; } = 1;
    public float ParticleDensity { get; set; } = 1f;
    public float EnvironmentDetail { get; set; } = 1.5f;
    public int GroundEffectDensity { get; set; } = 64;
    public int GroundEffectDist { get; set; } = 140;
    public int BaseMip { get; set; } = 1;
    public int ExtShadowQuality { get; set; } = 4;
    public int TextureFilteringMode { get; set; } = 5;
    public int WeatherDensity { get; set; } = 3;
    public int ComponentTextureLevel { get; set; } = 9;
    public bool Specular { get; set; } = true;
    public bool FfxDeath { get; set; } = true;
    public bool ProjectedTextures { get; set; } = true;
    public int SkyCloudLod { get; set; }
    public float WrathFarClip { get; set; } = WoWRenderLib.Structs.Wrath335FarClip.CVarMaximum;
    public bool WrathFarClipOverride { get; set; }
    public bool AnimateModels { get; set; } = true;
    public bool EnableWmoPortalCulling { get; set; }
    public bool ShowBoundingBoxes { get; set; }
    public bool ShowBoundingSpheres { get; set; }
    public bool ShowTerrainGrid { get; set; }
    public bool ShowTerrainWireframe { get; set; }
    public bool ShowTerrainTexture { get; set; } = true;
    public bool ShowTerrainVertexColor { get; set; } = true;

    public RendererSettings Clone() => new()
    {
        UseClientRenderingRules = UseClientRenderingRules,
        AmbientColor = AmbientColor,
        DiffuseColor = DiffuseColor,
        TerrainRenderDistance = TerrainRenderDistance,
        ModelRenderDistance = ModelRenderDistance,
        AnimationRenderDistancePercent = AnimationRenderDistancePercent,
        ParticleRenderDistancePercent = ParticleRenderDistancePercent,
        MinimumModelScreenSizePixels = MinimumModelScreenSizePixels,
        TerrainLodTransitionPixels = TerrainLodTransitionPixels,
        TileLoadingDistance = TileLoadingDistance,
        MovementSpeed = MovementSpeed,
        MouseSensitivity = MouseSensitivity,
        UseConfiguredLighting = UseConfiguredLighting,
        RenderADT = RenderADT,
        RenderLiquid = RenderLiquid,
        RenderWMO = RenderWMO,
        ShowWmoCollisionMesh = ShowWmoCollisionMesh,
        RenderM2 = RenderM2,
        RenderParticles = RenderParticles,
        RenderFog = RenderFog,
        EditorDisableScreenGlow = EditorDisableScreenGlow,
        DisableScreenGlow = DisableScreenGlow,
        ClientRenderingQuality = ClientRenderingQuality,
        TerrainMip = TerrainMip,
        ParticleDensity = ParticleDensity,
        EnvironmentDetail = EnvironmentDetail,
        GroundEffectDensity = GroundEffectDensity,
        GroundEffectDist = GroundEffectDist,
        BaseMip = BaseMip,
        ExtShadowQuality = ExtShadowQuality,
        TextureFilteringMode = TextureFilteringMode,
        WeatherDensity = WeatherDensity,
        ComponentTextureLevel = ComponentTextureLevel,
        Specular = Specular,
        FfxDeath = FfxDeath,
        ProjectedTextures = ProjectedTextures,
        SkyCloudLod = SkyCloudLod,
        WrathFarClip = WrathFarClip,
        WrathFarClipOverride = WrathFarClipOverride,
        AnimateModels = AnimateModels,
        EnableWmoPortalCulling = EnableWmoPortalCulling,
        ShowBoundingBoxes = ShowBoundingBoxes,
        ShowBoundingSpheres = ShowBoundingSpheres,
        ShowTerrainGrid = ShowTerrainGrid,
        ShowTerrainWireframe = ShowTerrainWireframe,
        ShowTerrainTexture = ShowTerrainTexture,
        ShowTerrainVertexColor = ShowTerrainVertexColor
    };
}
