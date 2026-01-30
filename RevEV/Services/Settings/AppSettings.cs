namespace RevEV.Services.Settings;

public class AppSettings : IAppSettings
{
    private const string KeyLastDeviceAddress = "last_device_address";
    private const string KeyLastDeviceName = "last_device_name";
    private const string KeySelectedProfile = "selected_engine_profile";
    private const string KeyUseMetric = "use_metric_units";
    private const string KeyMasterVolume = "master_volume";
    private const string KeySmoothingFactor = "smoothing_factor";
    private const string KeyAutoConnect = "auto_connect";
    private const string KeyHapticFeedback = "haptic_feedback";

    public string? LastConnectedDeviceAddress { get; set; }
    public string? LastConnectedDeviceName { get; set; }
    public string? SelectedEngineProfileId { get; set; }
    public bool UseMetricUnits { get; set; } = true;
    public float MasterVolume { get; set; } = 0.8f;
    public float SmoothingFactor { get; set; } = 0.15f;
    public bool AutoConnectOnLaunch { get; set; } = true;
    public bool EnableHapticFeedback { get; set; } = true;

    public AppSettings()
    {
        Load();
    }

    public void Save()
    {
        Preferences.Set(KeyLastDeviceAddress, LastConnectedDeviceAddress ?? string.Empty);
        Preferences.Set(KeyLastDeviceName, LastConnectedDeviceName ?? string.Empty);
        Preferences.Set(KeySelectedProfile, SelectedEngineProfileId ?? string.Empty);
        Preferences.Set(KeyUseMetric, UseMetricUnits);
        Preferences.Set(KeyMasterVolume, MasterVolume);
        Preferences.Set(KeySmoothingFactor, SmoothingFactor);
        Preferences.Set(KeyAutoConnect, AutoConnectOnLaunch);
        Preferences.Set(KeyHapticFeedback, EnableHapticFeedback);
    }

    public void Load()
    {
        LastConnectedDeviceAddress = GetStringOrNull(KeyLastDeviceAddress);
        LastConnectedDeviceName = GetStringOrNull(KeyLastDeviceName);
        SelectedEngineProfileId = GetStringOrNull(KeySelectedProfile);
        UseMetricUnits = Preferences.Get(KeyUseMetric, true);
        MasterVolume = Preferences.Get(KeyMasterVolume, 0.8f);
        SmoothingFactor = Preferences.Get(KeySmoothingFactor, 0.15f);
        AutoConnectOnLaunch = Preferences.Get(KeyAutoConnect, true);
        EnableHapticFeedback = Preferences.Get(KeyHapticFeedback, true);
    }

    public void Reset()
    {
        LastConnectedDeviceAddress = null;
        LastConnectedDeviceName = null;
        SelectedEngineProfileId = null;
        UseMetricUnits = true;
        MasterVolume = 0.8f;
        SmoothingFactor = 0.15f;
        AutoConnectOnLaunch = true;
        EnableHapticFeedback = true;
        Save();
    }

    private static string? GetStringOrNull(string key)
    {
        var value = Preferences.Get(key, string.Empty);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
