using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RevEV.Services.Settings;

namespace RevEV.Services.Bluetooth;

public partial class BluetoothManager : ObservableObject, IDisposable
{
    private readonly IBluetoothService _bleService;
    private readonly IAppSettings _settings;
    private readonly object _lock = new();
    private IBluetoothService? _classicService;
    private IBluetoothService? _activeService;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _reconnectCts;
    private volatile bool _isInitialized;
    private string? _lastError;
    private bool _disposed;

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

    private void RemoveServiceEvents(IBluetoothService service)
    {
        service.DeviceDiscovered -= OnDeviceDiscovered;
        service.ConnectionStateChanged -= OnConnectionStateChanged;
        service.DataReceived -= OnDataReceived;
        service.ErrorOccurred -= OnErrorOccurred;
    }

    public async Task<bool> InitializeAsync()
    {
        if (_disposed) return false;
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
        if (_disposed) return;
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

        // Dispose previous CTS if any
        _scanCts?.Dispose();
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
        if (_disposed) return false;
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
                    primaryService = _bleService;
                    fallbackService = _classicService;
                    break;
                default:
                    primaryService = _bleService;
                    fallbackService = _classicService;
                    break;
            }

            // Try primary service
            lock (_lock)
            {
                _activeService = primaryService;
            }
            var result = await primaryService.ConnectAsync(device);

            // If primary failed and we have a fallback, try it
            if (!result && fallbackService != null && fallbackService != primaryService)
            {
                StatusMessage = $"Retrying connection to {device.DisplayName}...";
                lock (_lock)
                {
                    _activeService = fallbackService;
                }
                result = await fallbackService.ConnectAsync(device);
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
                lock (_lock)
                {
                    _activeService = null;
                }
                StatusMessage = _lastError ?? "Connection failed. Make sure the device is nearby and powered on.";
            }

            return result;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            StatusMessage = $"Connection error: {ex.Message}";
            lock (_lock)
            {
                _activeService = null;
            }
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

        IBluetoothService? serviceToDisconnect;
        lock (_lock)
        {
            serviceToDisconnect = _activeService;
            _activeService = null;
        }

        if (serviceToDisconnect != null)
        {
            await serviceToDisconnect.DisconnectAsync();
        }

        ConnectedDevice = null;
        IsConnected = false;
        StatusMessage = "Disconnected";
    }

    public async Task<bool> AutoConnectAsync()
    {
        if (_disposed) return false;

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

        // Scan for the device
        DiscoveredDevices.Clear();

        // Dispose previous CTS
        _scanCts?.Dispose();
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

            // Wait for device discovery with polling
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

                try
                {
                    await Task.Delay(500, _scanCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
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

        // Final check
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
        if (_disposed) return false;

        IBluetoothService? service;
        lock (_lock)
        {
            service = _activeService;
        }

        if (service == null || !IsConnected)
        {
            return false;
        }

        return await service.WriteAsync(data);
    }

    public async Task<bool> WriteAsync(string command)
    {
        if (_disposed) return false;

        IBluetoothService? service;
        lock (_lock)
        {
            service = _activeService;
        }

        if (service == null || !IsConnected)
        {
            return false;
        }

        return await service.WriteAsync(command);
    }

    public void StartAutoReconnect()
    {
        if (_disposed) return;
        if (!_settings.AutoConnectOnLaunch) return;
        if (string.IsNullOrEmpty(_settings.LastConnectedDeviceAddress)) return;

        StopAutoReconnect();

        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        Task.Run(async () =>
        {
            int failedAttempts = 0;
            const int maxRetries = 5;

            try
            {
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
                                break;
                            }
                            else
                            {
                                failedAttempts++;
                            }
                        }
                        else if (IsConnected)
                        {
                            break;
                        }

                        // Exponential backoff with jitter
                        var baseDelay = 15 * Math.Pow(2, Math.Max(0, failedAttempts - 1));
                        var jitter = new Random().NextDouble() * 5;
                        var delay = TimeSpan.FromSeconds(Math.Min(baseDelay + jitter, 300));

                        await Task.Delay(delay, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-reconnect error: {ex.Message}");
                        failedAttempts++;
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-reconnect task error: {ex.Message}");
            }
        }, token);
    }

    public void StopAutoReconnect()
    {
        var cts = _reconnectCts;
        _reconnectCts = null;

        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void OnDeviceDiscovered(object? sender, BluetoothDevice device)
    {
        if (_disposed) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Avoid duplicates (thread-safe via main thread)
            if (!DiscoveredDevices.Any(d => d.Address == device.Address))
            {
                DiscoveredDevices.Add(device);
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, BluetoothConnectionState state)
    {
        if (_disposed) return;

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
        if (_disposed) return;
        DataReceived?.Invoke(this, data);
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        if (_disposed) return;

        _lastError = error;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusMessage = error;
            ErrorOccurred?.Invoke(this, error);
        });
    }

    public string? GetLastError() => _lastError;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            StopAutoReconnect();

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;

            // Unsubscribe from events
            RemoveServiceEvents(_bleService);
            if (_classicService != null)
            {
                RemoveServiceEvents(_classicService);
            }

            // Dispose services if they implement IDisposable
            if (_bleService is IDisposable disposableBle)
            {
                disposableBle.Dispose();
            }
            if (_classicService is IDisposable disposableClassic)
            {
                disposableClassic.Dispose();
            }
        }

        _disposed = true;
    }

    ~BluetoothManager()
    {
        Dispose(false);
    }
}
