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

    public async Task InitializeAsync()
    {
        await _bleService.InitializeAsync();
        if (_classicService != null)
        {
            await _classicService.InitializeAsync();
        }
    }

    public async Task StartScanningAsync()
    {
        if (IsScanning) return;

        DiscoveredDevices.Clear();
        IsScanning = true;
        StatusMessage = "Scanning...";

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
        finally
        {
            IsScanning = false;
            StatusMessage = DiscoveredDevices.Count > 0
                ? $"Found {DiscoveredDevices.Count} device(s)"
                : "No devices found";
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
        StatusMessage = $"Connecting to {device.DisplayName}...";

        try
        {
            // Select appropriate service based on device type
            _activeService = device.DeviceType switch
            {
                BluetoothDeviceType.Classic => _classicService ?? _bleService,
                BluetoothDeviceType.BLE => _bleService,
                _ => _bleService // Default to BLE
            };

            var result = await _activeService.ConnectAsync(device);

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
                StatusMessage = "Connection failed";
            }

            return result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
            return false;
        }

        StatusMessage = "Auto-connecting...";

        // First, scan for the device
        await StartScanningAsync();
        await Task.Delay(3000); // Give time to find the device
        await StopScanningAsync();

        // Look for the saved device
        var savedDevice = DiscoveredDevices.FirstOrDefault(d =>
            d.Address == _settings.LastConnectedDeviceAddress);

        if (savedDevice != null)
        {
            return await ConnectAsync(savedDevice);
        }

        StatusMessage = "Previously connected device not found";
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

        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsConnected && !IsConnecting && !string.IsNullOrEmpty(_settings.LastConnectedDeviceAddress))
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await AutoConnectAsync();
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(10), token);
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ErrorOccurred?.Invoke(this, error);
        });
    }
}
