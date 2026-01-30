namespace RevEV.Services.Settings;

public interface IAppSettings
{
    string? LastConnectedDeviceAddress { get; set; }
    string? LastConnectedDeviceName { get; set; }
    string? SelectedEngineProfileId { get; set; }
    bool UseMetricUnits { get; set; }
    float MasterVolume { get; set; }
    float SmoothingFactor { get; set; }
    bool AutoConnectOnLaunch { get; set; }
    bool EnableHapticFeedback { get; set; }

    void Save();
    void Load();
    void Reset();
}
