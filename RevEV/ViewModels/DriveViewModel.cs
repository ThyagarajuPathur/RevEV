using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevEV.Models;
using RevEV.Services.Audio;
using RevEV.Services.Bluetooth;
using RevEV.Services.Interpolation;
using RevEV.Services.Settings;

namespace RevEV.ViewModels;

public partial class DriveViewModel : BaseViewModel
{
    private readonly BluetoothManager _bluetoothManager;
    private readonly OBDProtocolHandler _obdHandler;
    private readonly IAudioEngine _audioEngine;
    private readonly IEngineProfileManager _profileManager;
    private readonly LerpInterpolator _interpolator;
    private readonly IAppSettings _settings;
    private System.Timers.Timer? _uiUpdateTimer;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private float _rpm;

    [ObservableProperty]
    private float _speed;

    [ObservableProperty]
    private float _rpmPercentage;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private string _currentProfileName = "No Profile";

    [ObservableProperty]
    private BluetoothDevice? _selectedDevice;

    public ObservableCollection<BluetoothDevice> DiscoveredDevices => _bluetoothManager.DiscoveredDevices;

    public DriveViewModel(
        BluetoothManager bluetoothManager,
        OBDProtocolHandler obdHandler,
        IAudioEngine audioEngine,
        IEngineProfileManager profileManager,
        LerpInterpolator interpolator,
        IAppSettings settings)
    {
        _bluetoothManager = bluetoothManager;
        _obdHandler = obdHandler;
        _audioEngine = audioEngine;
        _profileManager = profileManager;
        _interpolator = interpolator;
        _settings = settings;

        Title = "Drive";

        // Subscribe to events
        _bluetoothManager.ConnectionStateChanged += OnConnectionStateChanged;
        _obdHandler.DataUpdated += OnDataUpdated;

        // UI update timer (60 FPS)
        _uiUpdateTimer = new System.Timers.Timer(16.67);
        _uiUpdateTimer.Elapsed += OnUiTimerElapsed;
        _uiUpdateTimer.AutoReset = true;
        _uiUpdateTimer.Start();
    }

    public async Task InitializeAsync()
    {
        // Initialize Bluetooth first and handle failures gracefully
        var btInitialized = await _bluetoothManager.InitializeAsync();
        if (!btInitialized)
        {
            ConnectionStatus = _bluetoothManager.GetLastError() ?? "Bluetooth initialization failed";
        }

        await _profileManager.LoadProfilesAsync();
        await _audioEngine.InitializeAsync();

        if (_profileManager.CurrentProfile != null)
        {
            CurrentProfileName = _profileManager.CurrentProfile.Name;
            await _audioEngine.LoadProfileAsync(_profileManager.CurrentProfile);
        }

        // Auto-connect if enabled and Bluetooth is available
        if (btInitialized && _settings.AutoConnectOnLaunch && !string.IsNullOrEmpty(_settings.LastConnectedDeviceAddress))
        {
            await AutoConnectAsync();
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            await _bluetoothManager.StopScanningAsync();
            IsScanning = false;
            return;
        }

        IsScanning = true;
        ConnectionStatus = "Scanning for devices...";

        try
        {
            await _bluetoothManager.StartScanningAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = _bluetoothManager.IsScanning;
            ConnectionStatus = _bluetoothManager.StatusMessage;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync(BluetoothDevice? device)
    {
        if (device == null) return;

        ConnectionStatus = $"Connecting to {device.DisplayName}...";

        try
        {
            var result = await _bluetoothManager.ConnectAsync(device);

            if (result)
            {
                IsConnected = true;
                ConnectionStatus = $"Connected to {device.DisplayName}";

                // Initialize OBD and start polling
                var obdInit = await _obdHandler.InitializeAsync();
                if (obdInit)
                {
                    await _obdHandler.StartPollingAsync();
                }
                else
                {
                    ConnectionStatus = $"Connected but OBD init failed";
                }

                // Start audio playback
                if (_audioEngine.CurrentProfile != null)
                {
                    _audioEngine.Play();
                    IsPlaying = true;
                }
            }
            else
            {
                ConnectionStatus = _bluetoothManager.GetLastError() ?? "Connection failed";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Connection error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _obdHandler.StopPolling();
        _audioEngine.Stop();
        IsPlaying = false;

        await _bluetoothManager.DisconnectAsync();

        IsConnected = false;
        ConnectionStatus = "Disconnected";
        Rpm = 0;
        Speed = 0;
        RpmPercentage = 0;
    }

    [RelayCommand]
    private async Task AutoConnectAsync()
    {
        if (IsConnected) return;

        ConnectionStatus = "Auto-connecting...";
        var result = await _bluetoothManager.AutoConnectAsync();

        if (result)
        {
            IsConnected = true;
            await _obdHandler.InitializeAsync();
            await _obdHandler.StartPollingAsync();

            if (_audioEngine.CurrentProfile != null)
            {
                _audioEngine.Play();
                IsPlaying = true;
            }
        }
    }

    [RelayCommand]
    private void ToggleAudio()
    {
        if (IsPlaying)
        {
            _audioEngine.Stop();
            IsPlaying = false;
        }
        else if (_audioEngine.CurrentProfile != null)
        {
            _audioEngine.Play();
            IsPlaying = true;
        }
    }

    private void OnConnectionStateChanged(object? sender, BluetoothConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = state == BluetoothConnectionState.Connected;
            ConnectionStatus = _bluetoothManager.StatusMessage;

            if (state == BluetoothConnectionState.Disconnected)
            {
                _obdHandler.StopPolling();
                _audioEngine.Stop();
                IsPlaying = false;
            }
        });
    }

    private void OnDataUpdated(object? sender, VehicleData data)
    {
        // Update interpolator with new target values
        _interpolator.SetTargetValues(data);
    }

    private void OnUiTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Perform interpolation
        var interpolated = _interpolator.InterpolateAll();

        // Update audio engine
        _audioEngine.SetRpm(interpolated.InterpolatedRpm);

        // Update UI on main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Rpm = interpolated.InterpolatedRpm;
            Speed = interpolated.InterpolatedSpeed;
            RpmPercentage = interpolated.InterpolatedRpmPercentage;
        });
    }

    public void Cleanup()
    {
        // Stop and dispose timer
        if (_uiUpdateTimer != null)
        {
            _uiUpdateTimer.Stop();
            _uiUpdateTimer.Elapsed -= OnUiTimerElapsed;
            _uiUpdateTimer.Dispose();
            _uiUpdateTimer = null;
        }

        // Stop OBD polling
        _obdHandler?.StopPolling();

        // Stop audio
        _audioEngine?.Stop();

        // Unsubscribe from events
        if (_bluetoothManager != null)
        {
            _bluetoothManager.ConnectionStateChanged -= OnConnectionStateChanged;
        }

        if (_obdHandler != null)
        {
            _obdHandler.DataUpdated -= OnDataUpdated;
        }
    }
}
