using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public partial class LightingViewModel : ViewModelBase
{
    private bool _synchronizing;
    private bool _hasRendererSnapshot;

    public event EventHandler<LightingSettingsSnapshot>? Changed;

    [ObservableProperty] private long _lightParamId;
    [ObservableProperty] private long _time;
    [ObservableProperty] private bool _hasLiquidColorData;
    [ObservableProperty] private bool _hasLiquidAlphaData;

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
        ? $"LightData parameter {LightParamId}, time {Time}"
        : "Renderer defaults (client profile not loaded)";

    public void Update(LightingSettingsSnapshot lighting)
    {
        if (CreateSnapshot() == lighting)
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
        HasLiquidAlphaData);

    private void Publish()
    {
        // Avalonia controls can write their own default value back while the
        // panel is being attached. Do not let that replace LightParams values
        // before the renderer has supplied the active client profile.
        if (!_synchronizing && _hasRendererSnapshot)
            Changed?.Invoke(this, CreateSnapshot());
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
        var newRed = color.R / (float)byte.MaxValue;
        var newGreen = color.G / (float)byte.MaxValue;
        var newBlue = color.B / (float)byte.MaxValue;
        if (current.X == newRed && current.Y == newGreen && current.Z == newBlue)
            return;

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
            Publish();
    }

    private void PublishColor(string colorProperty)
    {
        OnPropertyChanged(colorProperty);
        Publish();
    }

    partial void OnDirectionXChanged(float value) => Publish();
    partial void OnDirectionYChanged(float value) => Publish();
    partial void OnDirectionZChanged(float value) => Publish();
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
    partial void OnWaterShallowAlphaChanged(float value) => Publish();
    partial void OnWaterDeepAlphaChanged(float value) => Publish();
    partial void OnOceanShallowAlphaChanged(float value) => Publish();
    partial void OnOceanDeepAlphaChanged(float value) => Publish();
}
