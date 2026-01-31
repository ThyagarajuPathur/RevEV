using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RevEV.Services.Settings;

namespace RevEV.Services.Bluetooth;

public partial class BluetoothManager : ObservableObject
{
    private readonly IBluetoothService _bleService;
    private readonly IAppSettings _settings;
    private IBluetoothService? _classicService;
    private IBluetoothService? _activeService;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _reconnectCts;
    private bool _isInitialized;
    private string? _lastError;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private BluetoothDevice? _connectedDevice;

    [ObservableProperty]
    private string _statusMessage = "Disconnected";

    [ObservableProperty]
    private bool _bluetoothEnabled = true;

    public ObservableCollection<BluetoothDevice> DiscoveredDevices { get; } = new();

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;

    public BluetoothManager(IBluetoothService bleService, IAppSettings settings)
    {
        _bleService = bleService;
        _settings = settings;

        // Try to create classic service (Android only)
#if ANDROID
        _classicService = new ClassicBluetoothService();
#endif

        SetupServiceEvents(_bleService);
        if (_classicService != null)
        {
            SetupServiceEvents(_classicService);
        }
    }

    private void SetupServiceEvents(IBluetoothService service)
    {
        service.DeviceDiscovered += OnDeviceDiscovered;
        service.ConnectionStateChanged += OnConnectionStateChanged;
        service.DataReceived += OnDataReceived;
        service.ErrorOccurred += OnErrorOccurred;
    }

    public async Task<bool> InitializeAsync()
    {
        if (_isInitialized) return true;

        _lastError = null;
        var bleResult = await _bleService.InitializeAsync();

        if (_classicService != null)
        {
            await _classicService.InitializeAsync();
        }

        if (!bleResult && !string.IsNullOrEmpty(_lastError))
        {
            StatusMessage = _lastError;
            BluetoothEnabled = false;
        }
        else
        {
            BluetoothEnabled = true;
        }

        _isInitialized = bleResult;
        return bleResult;
    }

    public async Task<bool> ReinitializeAsync()
    {
        _isInitialized = false;
        return await InitializeAsync();
    }

    public async Task StartScanningAsync()
    {
        if (IsScanning) return;

        // Clear any previous error
        _lastError = null;

        // Try to initialize if not already done
        if (!_isInitialized)
        {
            var initResult = await InitializeAsync();
            if (!initResult)
            {
                StatusMessage = _lastError ?? "Bluetooth initialization failed";
                return;
            }
        }

        DiscoveredDevices.Clear();
        IsScanning = true;
        StatusMessage = "Scanning for devices...";

        _scanCts = new CancellationTokenSource();
        _scanCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            // Scan with both services in parallel
            var tasks = new List<Task> { _bleService.StartScanningAsync(_scanCts.Token) };

            if (_classicService != null)
            {
                tasks.Add(_classicService.StartScanningAsync(_scanCts.Token));
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout or cancelled
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            IsScanning = false;
            if (!string.IsNullOrEmpty(_lastError))
            {
                StatusMessage = _lastError;
            }
            else if (DiscoveredDevices.Count > 0)
            {
                StatusMessage = $"Found {DiscoveredDevices.Count} device(s)";
            }
            else
            {
                StatusMessage = "No devices found. Make sure Bluetooth is enabled and devices are nearby.";
            }
        }
    }

    public async Task StopScanningAsync()
    {
        _scanCts?.Cancel();
        await _bleService.StopScanningAsync();
        if (_classicService != null)
        {
            await _classicService.StopScanningAsync();
        }
        IsScanning = false;
    }

    public async Task<bool> ConnectAsync(BluetoothDevice device)
    {
        if (IsConnecting || IsConnected) return false;

        IsConnecting = true;
        _lastError = null;
        StatusMessage = $"Connecting to {device.DisplayName}...";

        try
        {
            // Select appropriate service based on device type
            IBluetoothService primaryService;
            IBluetoothService? fallbackService = null;

            switch (device.DeviceType)
            {
                case BluetoothDeviceType.Classic:
                    primaryService = _classicService ?? _bleService;
                    fallbackService = _classicService != null ? _bleService : null;
                    break;
                case BluetoothDeviceType.BLE:
                    primaryService = _bleService;
                    fallbackService = _classicService;
                    break;
                case BluetoothDeviceType.Dual:
                    // Try BLE first for dual-mode devices, then Classic
                    primaryService = _bleService;
                    fallbackService = _classicService;
                    break;
                default:
                    primaryService = _bleService;
                    fallbackService = _classicService;
                    break;
            }

            // Try primary service
            _activeService = primaryService;
            var result = await _activeService.ConnectAsync(device);

            // If primary failed and we have a fallback, try it
            if (!result && fallbackService != null && fallbackService != primaryService)
            {
                StatusMessage = $"Retrying connection to {device.DisplayName}...";
                _activeService = fallbackService;
                result = await _activeService.ConnectAsync(device);
            }

            if (result)
            {
                ConnectedDevice = device;
                IsConnected = true;
                StatusMessage = $"Connected to {device.DisplayName}";

                // Save for auto-reconnect
                _settings.LastConnectedDeviceAddress = device.Address;
                _settings.LastConnectedDeviceName = device.DisplayName;
                _settings.Save();
            }
            else
            {
                _activeService = null;
                StatusMessage = _lastError ?? "Connection failed. Make sure the device is nearby and powered on.";
            }

            return result;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            StatusMessage = $"Connection error: {ex.Message}";
            _activeService = null;
            return false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public async Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();

        if (_activeService != null)
        {
            await _activeService.DisconnectAsync();
        }

        _activeService = null;
        ConnectedDevice = null;
        IsConnected = false;
        StatusMessage = "Disconnected";
    }

    public async Task<bool> AutoConnectAsync()
    {
        if (string.IsNullOrEmpty(_settings.LastConnectedDeviceAddress))
        {
            StatusMessage = "No previously connected device";
            return false;
        }

        if (IsConnected || IsConnecting)
        {
            return IsConnected;
        }

        StatusMessage = $"Looking for {_settings.LastConnectedDeviceName ?? "saved device"}...";

        // First, try to initialize
        if (!_isInitialized)
        {
            var initResult = await InitializeAsync();
            if (!initResult)
            {
                StatusMessage = _lastError ?? "Bluetooth not available";
                return false;
            }
        }

        // Scan for the device - use a longer timeout to find it
        DiscoveredDevices.Clear();
        _scanCts = new CancellationTokenSource();
        _scanCts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            IsScanning = true;

            // Start scanning
            var tasks = new List<Task> { _bleService.StartScanningAsync(_scanCts.Token) };
            if (_classicService != null)
            {
                tasks.Add(_classicService.StartScanningAsync(_scanCts.Token));
            }

            // Wait for device discovery with polling - stop early if we find our device
            var startTime = DateTime.UtcNow;
            var maxWait = TimeSpan.FromSeconds(10);

            while (DateTime.UtcNow - startTime < maxWait && !_scanCts.Token.IsCancellationRequested)
            {
                // Check if we found our device
                var savedDevice = DiscoveredDevices.FirstOrDefault(d =>
                    d.Address == _settings.LastConnectedDeviceAddress);

                if (savedDevice != null)
                {
                    await StopScanningAsync();
                    StatusMessage = $"Found {savedDevice.DisplayName}, connecting...";
                    return await ConnectAsync(savedDevice);
                }

                await Task.Delay(500, _scanCts.Token);
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            IsScanning = false;
            await StopScanningAsync();
        }

        // Final check - device might have been found just before timeout
        var device = DiscoveredDevices.FirstOrDefault(d =>
            d.Address == _settings.LastConnectedDeviceAddress);

        if (device != null)
        {
            return await ConnectAsync(device);
        }

        StatusMessage = $"Device '{_settings.LastConnectedDeviceName ?? "Unknown"}' not found nearby";
        return false;
    }

    public async Task<bool> WriteAsync(byte[] data)
    {
        if (_activeService == null || !IsConnected)
        {
            return false;
        }

        return await _activeService.WriteAsync(data);
    }

    public async Task<bool> WriteAsync(string command)
    {
        if (_activeService == null || !IsConnected)
        {
            return false;
        }

        return await _activeService.WriteAsync(command);
    }

    public void StartAutoReconnect()
    {
        if (!_settings.AutoConnectOnLaunch) return;
        if (string.IsNullOrEmpty(_settings.LastConnectedDeviceAddress)) return;

        StopAutoReconnect(); // Stop any existing reconnect loop

        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        Task.Run(async () =>
        {
            int failedAttempts = 0;
            const int maxRetries = 5;

            while (!token.IsCancellationRequested && failedAttempts < maxRetries)
            {
                try
                {
                    if (!IsConnected && !IsConnecting && !IsScanning &&
                        !string.IsNullOrEmpty(_settings.LastConnectedDeviceAddress))
                    {
                        var result = await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            return await AutoConnectAsync();
                        });

                        if (result)
                        {
                            // Successfully connected - exit the loop
                            break;
                        }
                        else
                        {
                            failedAttempts++;
                        }
                    }
                    else if (IsConnected)
                    {
                        // Already connected - exit
                        break;
                    }

                    // Exponential backoff: 15s, 30s, 60s, 120s, 240s
                    var delay = TimeSpan.FromSeconds(15 * Math.Pow(2, failedAttempts - 1));
                    if (delay > TimeSpan.FromMinutes(5)) delay = TimeSpan.FromMinutes(5);

                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    failedAttempts++;
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
            }
        }, token);
    }

    public void StopAutoReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
    }

    private void OnDeviceDiscovered(object? sender, BluetoothDevice device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Avoid duplicates
            if (!DiscoveredDevices.Any(d => d.Address == device.Address))
            {
                DiscoveredDevices.Add(device);
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, BluetoothConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = state == BluetoothConnectionState.Connected;
            IsConnecting = state == BluetoothConnectionState.Connecting;

            StatusMessage = state switch
            {
                BluetoothConnectionState.Connected => $"Connected to {ConnectedDevice?.DisplayName}",
                BluetoothConnectionState.Connecting => "Connecting...",
                BluetoothConnectionState.Disconnecting => "Disconnecting...",
                BluetoothConnectionState.Disconnected => "Disconnected",
                BluetoothConnectionState.Error => "Connection error",
                _ => StatusMessage
            };

            if (state == BluetoothConnectionState.Disconnected && ConnectedDevice != null)
            {
                ConnectedDevice = null;
            }

            ConnectionStateChanged?.Invoke(this, state);
        });
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        DataReceived?.Invoke(this, data);
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        _lastError = error;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusMessage = error;
            ErrorOccurred?.Invoke(this, error);
        });
    }

    public string? GetLastError() => _lastError;
}
