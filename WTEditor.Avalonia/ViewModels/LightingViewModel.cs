using System.Numerics;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public sealed record ActiveLightDisplayItem(string Description);
public sealed record LightingValueDisplayItem(string Label, string Value, Color? PreviewColor = null)
{
    public bool HasPreview => PreviewColor.HasValue;
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
    public IBrush? PreviewBrush => PreviewColor.HasValue
        ? new SolidColorBrush(PreviewColor.Value)
        : null;
}
public sealed record LightingGroupDisplayItem(
    string Title,
    string Status,
    IReadOnlyList<LightingValueDisplayItem> Values);

public partial class LightingViewModel : ViewModelBase
{
    private bool _synchronizing;
    private bool _hasRendererSnapshot;
    private IReadOnlyList<ActiveLightingSnapshot> _activeLightSnapshots =
        Array.Empty<ActiveLightingSnapshot>();
    private LightingRuntimeSnapshot? _runtimeSnapshot;

    public event EventHandler<LightingSettingsSnapshot>? Changed;

    [ObservableProperty] private long _lightParamId;
    [ObservableProperty] private long _time = 1440;
    [ObservableProperty] private bool _hasLiquidColorData;
    [ObservableProperty] private bool _hasLiquidAlphaData;
    [ObservableProperty] private bool _isDynamic = false;
    [ObservableProperty] private IReadOnlyList<ActiveLightDisplayItem> _activeLights =
        Array.Empty<ActiveLightDisplayItem>();
    [ObservableProperty] private IReadOnlyList<LightingGroupDisplayItem> _runtimeGroups =
        Array.Empty<LightingGroupDisplayItem>();

    [ObservableProperty] private float _directionX = -0.5f;
    [ObservableProperty] private float _directionY = -0.5f;
    [ObservableProperty] private float _directionZ = 0.70710678f;
    [ObservableProperty] private float _ambientR = 104f / 255f;
    [ObservableProperty] private float _ambientG = 130f / 255f;
    [ObservableProperty] private float _ambientB = 154f / 255f;
    [ObservableProperty] private float _diffuseR = 1f;
    [ObservableProperty] private float _diffuseG = 136f / 255f;
    [ObservableProperty] private float _diffuseB;
    [ObservableProperty] private float _oceanCloseR;
    [ObservableProperty] private float _oceanCloseG;
    [ObservableProperty] private float _oceanCloseB;
    [ObservableProperty] private float _oceanFarR;
    [ObservableProperty] private float _oceanFarG;
    [ObservableProperty] private float _oceanFarB;
    [ObservableProperty] private float _riverCloseR;
    [ObservableProperty] private float _riverCloseG;
    [ObservableProperty] private float _riverCloseB;
    [ObservableProperty] private float _riverFarR;
    [ObservableProperty] private float _riverFarG;
    [ObservableProperty] private float _riverFarB;
    [ObservableProperty] private float _waterShallowAlpha = 1f;
    [ObservableProperty] private float _waterDeepAlpha = 1f;
    [ObservableProperty] private float _oceanShallowAlpha = 1f;
    [ObservableProperty] private float _oceanDeepAlpha = 1f;

    public Color AmbientColor
    {
        get => ToPickerColor(AmbientR, AmbientG, AmbientB);
        set => SetPickerColor(value, new Vector3(AmbientR, AmbientG, AmbientB),
            (red, green, blue) => (AmbientR, AmbientG, AmbientB) = (red, green, blue));
    }

    public Color DiffuseColor
    {
        get => ToPickerColor(DiffuseR, DiffuseG, DiffuseB);
        set => SetPickerColor(value, new Vector3(DiffuseR, DiffuseG, DiffuseB),
            (red, green, blue) => (DiffuseR, DiffuseG, DiffuseB) = (red, green, blue));
    }

    public Color OceanCloseColor
    {
        get => ToPickerColor(OceanCloseR, OceanCloseG, OceanCloseB);
        set => SetPickerColor(value, new Vector3(OceanCloseR, OceanCloseG, OceanCloseB),
            (red, green, blue) => (OceanCloseR, OceanCloseG, OceanCloseB) = (red, green, blue));
    }

    public Color OceanFarColor
    {
        get => ToPickerColor(OceanFarR, OceanFarG, OceanFarB);
        set => SetPickerColor(value, new Vector3(OceanFarR, OceanFarG, OceanFarB),
            (red, green, blue) => (OceanFarR, OceanFarG, OceanFarB) = (red, green, blue));
    }

    public Color RiverCloseColor
    {
        get => ToPickerColor(RiverCloseR, RiverCloseG, RiverCloseB);
        set => SetPickerColor(value, new Vector3(RiverCloseR, RiverCloseG, RiverCloseB),
            (red, green, blue) => (RiverCloseR, RiverCloseG, RiverCloseB) = (red, green, blue));
    }

    public Color RiverFarColor
    {
        get => ToPickerColor(RiverFarR, RiverFarG, RiverFarB);
        set => SetPickerColor(value, new Vector3(RiverFarR, RiverFarG, RiverFarB),
            (red, green, blue) => (RiverFarR, RiverFarG, RiverFarB) = (red, green, blue));
    }

    public string ProfileDescription => LightParamId > 0
        ? $"{(IsDynamic ? "Dynamic " : string.Empty)}LightData parameter {LightParamId}, time {Time}"
        : "Renderer defaults (client profile not loaded)";

    public string ActiveLightsStatus => ActiveLights.Count > 0
        ? "Current contributors to the final blended values:"
        : "No active spatial light contributors reported.";

    public void SetPreferences(int time, bool useLocalTime)
    {
        _synchronizing = true;
        try
        {
            Time = time;
            IsDynamic = useLocalTime;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    public void Update(LightingSettingsSnapshot lighting)
    {
        if (Matches(lighting))
        {
            _hasRendererSnapshot = true;
            return;
        }

        _synchronizing = true;
        try
        {
            LightParamId = lighting.LightParamId;
            Time = lighting.Time;
            HasLiquidColorData = lighting.HasLiquidColorData;
            HasLiquidAlphaData = lighting.HasLiquidAlphaData;
            IsDynamic = lighting.IsDynamic;
            DirectionX = lighting.LightDirection.X;
            DirectionY = lighting.LightDirection.Y;
            DirectionZ = lighting.LightDirection.Z;
            AmbientR = lighting.AmbientColor.X;
            AmbientG = lighting.AmbientColor.Y;
            AmbientB = lighting.AmbientColor.Z;
            DiffuseR = lighting.DiffuseColor.X;
            DiffuseG = lighting.DiffuseColor.Y;
            DiffuseB = lighting.DiffuseColor.Z;
            OceanCloseR = lighting.OceanCloseColor.X;
            OceanCloseG = lighting.OceanCloseColor.Y;
            OceanCloseB = lighting.OceanCloseColor.Z;
            OceanFarR = lighting.OceanFarColor.X;
            OceanFarG = lighting.OceanFarColor.Y;
            OceanFarB = lighting.OceanFarColor.Z;
            RiverCloseR = lighting.RiverCloseColor.X;
            RiverCloseG = lighting.RiverCloseColor.Y;
            RiverCloseB = lighting.RiverCloseColor.Z;
            RiverFarR = lighting.RiverFarColor.X;
            RiverFarG = lighting.RiverFarColor.Y;
            RiverFarB = lighting.RiverFarColor.Z;
            WaterShallowAlpha = lighting.WaterShallowAlpha;
            WaterDeepAlpha = lighting.WaterDeepAlpha;
            OceanShallowAlpha = lighting.OceanShallowAlpha;
            OceanDeepAlpha = lighting.OceanDeepAlpha;
            SetActiveLights(lighting.ActiveLights);
            SetRuntimeGroups(lighting.Runtime, lighting);
            OnPropertyChanged(nameof(ProfileDescription));
        }
        finally
        {
            _synchronizing = false;
            _hasRendererSnapshot = true;
        }
    }

    private LightingSettingsSnapshot CreateSnapshot() => new(
        LightParamId,
        Time,
        new Vector3(DirectionX, DirectionY, DirectionZ),
        new Vector3(AmbientR, AmbientG, AmbientB),
        new Vector3(DiffuseR, DiffuseG, DiffuseB),
        new Vector3(OceanCloseR, OceanCloseG, OceanCloseB),
        new Vector3(OceanFarR, OceanFarG, OceanFarB),
        new Vector3(RiverCloseR, RiverCloseG, RiverCloseB),
        new Vector3(RiverFarR, RiverFarG, RiverFarB),
        WaterShallowAlpha,
        WaterDeepAlpha,
        OceanShallowAlpha,
        OceanDeepAlpha,
        HasLiquidColorData,
        HasLiquidAlphaData,
        IsDynamic,
        _activeLightSnapshots,
        _runtimeSnapshot);

    private bool Matches(LightingSettingsSnapshot lighting)
    {
        var current = CreateSnapshot() with { ActiveLights = null, Runtime = null };
        var incoming = lighting with { ActiveLights = null, Runtime = null };
        return current == incoming &&
            ActiveLightSnapshotsEqual(_activeLightSnapshots, lighting.ActiveLights) &&
            RuntimeGroupsEqual(RuntimeGroups, CreateRuntimeGroups(lighting.Runtime, lighting));
    }

    private void SetActiveLights(IReadOnlyList<ActiveLightingSnapshot>? activeLights)
    {
        _activeLightSnapshots = activeLights is { Count: > 0 }
            ? activeLights.ToArray()
            : Array.Empty<ActiveLightingSnapshot>();
        ActiveLights = _activeLightSnapshots
            .Select((light, index) => new ActiveLightDisplayItem(
                $"{index + 1}: {FormatSource(light)} (id {light.LightId}) — " +
                $"{light.Weight:P0} · LightData {light.LightParamId}"))
            .ToArray();
        OnPropertyChanged(nameof(ActiveLightsStatus));
    }

    private static string FormatSource(ActiveLightingSnapshot light)
    {
        var source = light.SourceKind switch
        {
            ActiveLightingSourceKind.Global => "Global light",
            ActiveLightingSourceKind.Zone => "Zone light",
            _ => "Local light"
        };
        if (light.SourceKind == ActiveLightingSourceKind.Zone &&
            light.ZoneLightId > 0 &&
            !string.IsNullOrWhiteSpace(light.ZoneName))
        {
            source += $" · {light.ZoneName} (zone {light.ZoneLightId})";
        }

        return source;
    }

    private static bool ActiveLightSnapshotsEqual(
        IReadOnlyList<ActiveLightingSnapshot> current,
        IReadOnlyList<ActiveLightingSnapshot>? incoming)
    {
        if (incoming == null || current.Count != incoming.Count)
            return incoming == null && current.Count == 0;

        for (var index = 0; index < current.Count; index++)
        {
            if (current[index] != incoming[index])
                return false;
        }

        return true;
    }

    private void SetRuntimeGroups(
        LightingRuntimeSnapshot? runtime,
        LightingSettingsSnapshot lighting)
    {
        _runtimeSnapshot = runtime;
        RuntimeGroups = CreateRuntimeGroups(runtime, lighting);
    }

    private static IReadOnlyList<LightingGroupDisplayItem> CreateRuntimeGroups(
        LightingRuntimeSnapshot? runtime,
        LightingSettingsSnapshot lighting)
    {
        if (runtime == null)
            return Array.Empty<LightingGroupDisplayItem>();

        var liquidStatus = lighting.HasLiquidColorData
            ? "Final blended values"
            : "Renderer material fallback";
        var skyStatus = runtime.HasSkyColorData ? "Final blended values" : "No client sky palette reported";
        var sunStatus = runtime.HasSunCloudData ? "Final blended values" : "No client sun/cloud palette reported";
        var fogStatus = runtime.HasFogData ? "Final blended values" : "No client fog settings reported";
        var skyboxes = runtime.Skyboxes.Count == 0
            ? "None"
            : string.Join(
                ", ",
                runtime.Skyboxes.Select(static skybox =>
                    $"{skybox.FileDataId} ({skybox.Opacity:P0}, flags 0x{skybox.Flags:X})"));

        return
        [
            new LightingGroupDisplayItem(
                "Liquid",
                liquidStatus,
                [
                    ColorValue("Ocean close", lighting.OceanCloseColor),
                    ColorValue("Ocean far", lighting.OceanFarColor),
                    ColorValue("River close", lighting.RiverCloseColor),
                    ColorValue("River far", lighting.RiverFarColor),
                    NumberValue("Water shallow", lighting.WaterShallowAlpha),
                    NumberValue("Water deep", lighting.WaterDeepAlpha),
                    NumberValue("Ocean shallow", lighting.OceanShallowAlpha),
                    NumberValue("Ocean deep", lighting.OceanDeepAlpha)
                ]),
            new LightingGroupDisplayItem(
                "Sky",
                skyStatus,
                [
                    ColorValue("Top", runtime.SkyTopColor),
                    ColorValue("Middle", runtime.SkyMiddleColor),
                    ColorValue("Band 1", runtime.SkyBand1Color),
                    ColorValue("Band 2", runtime.SkyBand2Color),
                    ColorValue("Smog", runtime.SkySmogColor),
                    ColorValue("Fog horizon", runtime.SkyFogColor),
                    new("Highlight sky", runtime.HighlightSky ? "On" : "Off"),
                    new("Skybox layers", skyboxes)
                ]),
            new LightingGroupDisplayItem(
                "Sun",
                sunStatus,
                [ColorValue("Sun color", runtime.SunColor)]),
            new LightingGroupDisplayItem(
                "Clouds",
                sunStatus,
                [
                    ColorValue("Sun light", runtime.CloudSunColor),
                    ColorValue("Emissive", runtime.CloudEmissiveColor),
                    ColorValue("Layer 1 ambient", runtime.CloudLayer1AmbientColor),
                    ColorValue("Layer 2 ambient", runtime.CloudLayer2AmbientColor),
                    NumberValue("Density", runtime.CloudDensity)
                ]),
            new LightingGroupDisplayItem(
                "Fog",
                fogStatus,
                [
                    NumberValue("End", runtime.FogEnd),
                    NumberValue("Scaler", runtime.FogScaler),
                    NumberValue("Density", runtime.FogDensity),
                    NumberValue("Height", runtime.FogHeight),
                    NumberValue("Height scaler", runtime.FogHeightScaler),
                    NumberValue("Height density", runtime.FogHeightDensity),
                    NumberValue("Z scalar", runtime.FogZScalar),
                    NumberValue("Main start", runtime.MainFogStartDistance),
                    NumberValue("Main end", runtime.MainFogEndDistance),
                    NumberValue("Sun angle", runtime.SunFogAngle),
                    ColorValue("End color", runtime.EndFogColor),
                    NumberValue("End color distance", runtime.EndFogColorDistance),
                    NumberValue("Start offset", runtime.FogStartOffset),
                    ColorValue("Sun fog color", runtime.SunFogColor),
                    NumberValue("Sun fog strength", runtime.SunFogStrength),
                    ColorValue("Height color", runtime.FogHeightColor),
                    ColorValue("End height color", runtime.EndFogHeightColor),
                    ColorValue("Ground ambient", runtime.GroundAmbientColor),
                    ColorValue("Horizon ambient", runtime.HorizonAmbientColor),
                    VectorValue("Height coefficients", runtime.FogHeightCoefficients),
                    VectorValue("Main coefficients", runtime.MainFogCoefficients),
                    VectorValue("Height density coefficients", runtime.HeightDensityFogCoefficients),
                    new("Shadow opacity", FormatNumber(runtime.ShadowOpacity)),
                    new("Color grading", FormatId(runtime.ColorGradingFileDataId)),
                    new("Darker grading", FormatId(runtime.DarkerColorGradingFileDataId))
                ])
        ];
    }

    private static LightingValueDisplayItem ColorValue(string label, Vector3 value)
    {
        var color = ToPickerColor(value.X, value.Y, value.Z);
        return new(label, string.Empty, color);
    }

    private static LightingValueDisplayItem NumberValue(string label, float value) =>
        new(label, FormatNumber(value));

    private static LightingValueDisplayItem VectorValue(string label, Vector4 value) =>
        new(label, $"({FormatNumber(value.X)}, {FormatNumber(value.Y)}, {FormatNumber(value.Z)}, {FormatNumber(value.W)})");

    private static string FormatId(long value) => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "None";

    private static string FormatNumber(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool RuntimeGroupsEqual(
        IReadOnlyList<LightingGroupDisplayItem> current,
        IReadOnlyList<LightingGroupDisplayItem> incoming)
    {
        if (current.Count != incoming.Count)
            return false;
        for (var groupIndex = 0; groupIndex < current.Count; groupIndex++)
        {
            if (current[groupIndex].Title != incoming[groupIndex].Title ||
                current[groupIndex].Status != incoming[groupIndex].Status ||
                current[groupIndex].Values.Count != incoming[groupIndex].Values.Count)
            {
                return false;
            }
            for (var valueIndex = 0; valueIndex < current[groupIndex].Values.Count; valueIndex++)
            {
                var currentValue = current[groupIndex].Values[valueIndex];
                var incomingValue = incoming[groupIndex].Values[valueIndex];
                if (currentValue.Label != incomingValue.Label ||
                    currentValue.Value != incomingValue.Value ||
                    currentValue.PreviewColor != incomingValue.PreviewColor)
                    return false;
            }
        }
        return true;
    }

    private void Publish()
    {
        // Avalonia controls can write their own default value back while the
        // panel is being attached. Do not let that replace LightParams values
        // before the renderer has supplied the active client profile.
        if (!_synchronizing && _hasRendererSnapshot)
            Changed?.Invoke(this, CreateSnapshot());
    }

    private void PublishManualEdit()
    {
        // Dynamic values are renderer-owned. Avalonia controls can finish a
        // delayed TwoWay write after the renderer snapshot has synchronized;
        // treating that write as a manual edit would silently disable spatial
        // lighting on the first map load. Manual edits are accepted only after
        // the user explicitly turns off live local time.
        if (_synchronizing || IsDynamic)
            return;

        SetActiveLights(null);
        Publish();
    }

    private static Color ToPickerColor(float red, float green, float blue) => Color.FromRgb(
        ToByte(red),
        ToByte(green),
        ToByte(blue));

    private static byte ToByte(float value) =>
        (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue);

    private void SetPickerColor(
        Color color,
        Vector3 current,
        Action<float, float, float> apply)
    {
        // Renderer lighting is stored as floats, while Avalonia's ColorPicker
        // can only represent 8-bit channels. A TwoWay binding may write the
        // displayed, quantized color back after a renderer snapshot. Treat
        // that as synchronization rather than a manual edit; otherwise the
        // harmless round-trip disables dynamic spatial lighting.
        if (ToPickerColor(current.X, current.Y, current.Z) == color)
            return;

        var newRed = color.R / (float)byte.MaxValue;
        var newGreen = color.G / (float)byte.MaxValue;
        var newBlue = color.B / (float)byte.MaxValue;

        var wasSynchronizing = _synchronizing;
        _synchronizing = true;
        try
        {
            apply(newRed, newGreen, newBlue);
        }
        finally
        {
            _synchronizing = wasSynchronizing;
        }

        if (!wasSynchronizing)
            PublishManualEdit();
    }

    private void PublishColor(string colorProperty)
    {
        OnPropertyChanged(colorProperty);
        PublishManualEdit();
    }

    partial void OnIsDynamicChanged(bool value)
    {
        OnPropertyChanged(nameof(ProfileDescription));
        if (!value && !_synchronizing)
            SetActiveLights(null);
        Publish();
    }
    partial void OnTimeChanged(long value)
    {
        OnPropertyChanged(nameof(ProfileDescription));
        // The disabled Avalonia slider can echo its tick-snapped display value
        // after a dynamic renderer snapshot has finished synchronizing. A time
        // write cannot be a manual edit while live time is enabled, so never
        // let that control echo turn dynamic lighting off.
        if (!IsDynamic)
            PublishManualEdit();
    }
    partial void OnDirectionXChanged(float value) => PublishManualEdit();
    partial void OnDirectionYChanged(float value) => PublishManualEdit();
    partial void OnDirectionZChanged(float value) => PublishManualEdit();
    partial void OnAmbientRChanged(float value) => PublishColor(nameof(AmbientColor));
    partial void OnAmbientGChanged(float value) => PublishColor(nameof(AmbientColor));
    partial void OnAmbientBChanged(float value) => PublishColor(nameof(AmbientColor));
    partial void OnDiffuseRChanged(float value) => PublishColor(nameof(DiffuseColor));
    partial void OnDiffuseGChanged(float value) => PublishColor(nameof(DiffuseColor));
    partial void OnDiffuseBChanged(float value) => PublishColor(nameof(DiffuseColor));
    partial void OnOceanCloseRChanged(float value) => PublishColor(nameof(OceanCloseColor));
    partial void OnOceanCloseGChanged(float value) => PublishColor(nameof(OceanCloseColor));
    partial void OnOceanCloseBChanged(float value) => PublishColor(nameof(OceanCloseColor));
    partial void OnOceanFarRChanged(float value) => PublishColor(nameof(OceanFarColor));
    partial void OnOceanFarGChanged(float value) => PublishColor(nameof(OceanFarColor));
    partial void OnOceanFarBChanged(float value) => PublishColor(nameof(OceanFarColor));
    partial void OnRiverCloseRChanged(float value) => PublishColor(nameof(RiverCloseColor));
    partial void OnRiverCloseGChanged(float value) => PublishColor(nameof(RiverCloseColor));
    partial void OnRiverCloseBChanged(float value) => PublishColor(nameof(RiverCloseColor));
    partial void OnRiverFarRChanged(float value) => PublishColor(nameof(RiverFarColor));
    partial void OnRiverFarGChanged(float value) => PublishColor(nameof(RiverFarColor));
    partial void OnRiverFarBChanged(float value) => PublishColor(nameof(RiverFarColor));
    partial void OnWaterShallowAlphaChanged(float value) => PublishManualEdit();
    partial void OnWaterDeepAlphaChanged(float value) => PublishManualEdit();
    partial void OnOceanShallowAlphaChanged(float value) => PublishManualEdit();
    partial void OnOceanDeepAlphaChanged(float value) => PublishManualEdit();
}
