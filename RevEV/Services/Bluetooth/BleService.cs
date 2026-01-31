using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using System.Text;

namespace RevEV.Services.Bluetooth;

public class BleService : IBluetoothService, IDisposable
{
    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;
    private readonly object _lock = new();
    private IDevice? _connectedDevice;
    private ICharacteristic? _writeCharacteristic;
    private ICharacteristic? _readCharacteristic;
    private bool _isInitialized;
    private bool _permissionsGranted;
    private bool _showAllDevices = true;
    private bool _disposed;
    private bool _characteristicHandlerRegistered;

    // Common ELM327 BLE Service and Characteristic UUIDs
    private static readonly Guid[] ServiceUuids = new[]
    {
        Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb"), // Common ELM327 service
        Guid.Parse("0000ffe0-0000-1000-8000-00805f9b34fb"), // Alternative ELM327 service
        Guid.Parse("e7810a71-73ae-499d-8c15-faa9aef0c3f2"), // Vgate iCar Pro
    };

    private static readonly Guid[] WriteCharacteristicUuids = new[]
    {
        Guid.Parse("0000fff2-0000-1000-8000-00805f9b34fb"),
        Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"),
        Guid.Parse("bef8d6c9-9c21-4c9e-b632-bd58c1009f9f"),
    };

    private static readonly Guid[] ReadCharacteristicUuids = new[]
    {
        Guid.Parse("0000fff1-0000-1000-8000-00805f9b34fb"),
        Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"),
        Guid.Parse("bef8d6c9-9c21-4c9e-b632-bd58c1009f9f"),
    };

    public BluetoothServiceType ServiceType => BluetoothServiceType.BLE;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _connectedDevice?.State == DeviceState.Connected;
            }
        }
    }

    public bool IsScanning => _adapter.IsScanning;

    public event EventHandler<BluetoothDevice>? DeviceDiscovered;
    public event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;
    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;

    public BleService()
    {
        _ble = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.DeviceConnected += OnDeviceConnected;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLost;
    }

    public async Task<bool> InitializeAsync()
    {
        if (_disposed) return false;
        if (_isInitialized && _permissionsGranted) return true;

        try
        {
            // Request permissions first (before checking Bluetooth state)
#if ANDROID
            // Request all Bluetooth permissions on Android
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.S)
            {
                // Android 12+ uses new Bluetooth permissions
                var btStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
                if (btStatus != PermissionStatus.Granted)
                {
                    btStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
                    if (btStatus != PermissionStatus.Granted)
                    {
                        ErrorOccurred?.Invoke(this, "Bluetooth permission denied. Please enable in Settings.");
                        return false;
                    }
                }
            }
            else
            {
                // Android < 12 requires Location for BLE scanning
                var locationStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (locationStatus != PermissionStatus.Granted)
                {
                    locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    if (locationStatus != PermissionStatus.Granted)
                    {
                        ErrorOccurred?.Invoke(this, "Location permission required for Bluetooth scanning. Please enable in Settings.");
                        return false;
                    }
                }
            }
            _permissionsGranted = true;
#elif IOS
            _permissionsGranted = true;
#endif

            // Check Bluetooth state - wait briefly if it's transitioning
            int retries = 0;
            while (_ble.State == BluetoothState.Unknown && retries < 10)
            {
                await Task.Delay(100);
                retries++;
            }

            if (_ble.State == BluetoothState.Unavailable)
            {
                ErrorOccurred?.Invoke(this, "Bluetooth is not available on this device");
                return false;
            }

            if (_ble.State == BluetoothState.Unauthorized)
            {
                ErrorOccurred?.Invoke(this, "Bluetooth access not authorized. Please enable in Settings.");
                return false;
            }

            if (_ble.State != BluetoothState.On)
            {
                ErrorOccurred?.Invoke(this, "Please turn on Bluetooth to scan for devices");
                return false;
            }

            _adapter.ScanTimeout = 30000; // 30 seconds
            _adapter.ScanMode = ScanMode.LowLatency; // Faster scanning
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Initialization failed: {ex.Message}");
            return false;
        }
    }

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        // Always try to initialize if not done yet
        if (!_isInitialized || !_permissionsGranted)
        {
            var initResult = await InitializeAsync();
            if (!initResult)
            {
                return;
            }
        }

        // Re-check Bluetooth state before scanning
        if (_ble.State != BluetoothState.On)
        {
            ErrorOccurred?.Invoke(this, "Please turn on Bluetooth to scan for devices");
            return;
        }

        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }

        try
        {
            await _adapter.StartScanningForDevicesAsync(
                serviceUuids: null,
                deviceFilter: device =>
                {
                    if (_showAllDevices)
                    {
                        return true;
                    }
                    if (string.IsNullOrEmpty(device.Name)) return false;
                    return device.Name.Contains("OBD", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("ELM", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("VLINK", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("iCar", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("VEEPEAK", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("V-LINK", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("OBDII", StringComparison.OrdinalIgnoreCase) ||
                           device.Name.Contains("OBD2", StringComparison.OrdinalIgnoreCase);
                },
                allowDuplicatesKey: false,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Scanning was cancelled - this is expected
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Scanning failed: {ex.Message}");
        }
    }

    public void SetShowAllDevices(bool showAll)
    {
        _showAllDevices = showAll;
    }

    public async Task StopScanningAsync()
    {
        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }
    }

    public async Task<bool> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        // Validate device BEFORE changing state
        if (device.NativeDevice is not IDevice nativeDevice)
        {
            ErrorOccurred?.Invoke(this, "Invalid device reference");
            return false;
        }

        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connecting);

            var connectParams = new ConnectParameters(
                autoConnect: false,
                forceBleTransport: true);

            await _adapter.ConnectToDeviceAsync(nativeDevice, connectParams, cancellationToken);

            lock (_lock)
            {
                _connectedDevice = nativeDevice;
            }

            // Discover services and characteristics
            var services = await nativeDevice.GetServicesAsync();
            bool foundCharacteristics = false;

            foreach (var service in services)
            {
                // Check if this is an OBD service
                if (!ServiceUuids.Contains(service.Id) &&
                    !service.Id.ToString().Contains("ffe", StringComparison.OrdinalIgnoreCase) &&
                    !service.Id.ToString().Contains("fff", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var characteristics = await service.GetCharacteristicsAsync();

                foreach (var characteristic in characteristics)
                {
                    // Find write characteristic
                    if (_writeCharacteristic == null && characteristic.CanWrite)
                    {
                        if (WriteCharacteristicUuids.Contains(characteristic.Id) ||
                            characteristic.Id.ToString().Contains("ffe1", StringComparison.OrdinalIgnoreCase) ||
                            characteristic.Id.ToString().Contains("fff2", StringComparison.OrdinalIgnoreCase))
                        {
                            _writeCharacteristic = characteristic;
                        }
                    }

                    // Find read/notify characteristic
                    if (_readCharacteristic == null &&
                        (characteristic.CanRead || characteristic.CanUpdate))
                    {
                        if (ReadCharacteristicUuids.Contains(characteristic.Id) ||
                            characteristic.Id.ToString().Contains("ffe1", StringComparison.OrdinalIgnoreCase) ||
                            characteristic.Id.ToString().Contains("fff1", StringComparison.OrdinalIgnoreCase))
                        {
                            _readCharacteristic = characteristic;

                            // Subscribe to notifications (only if not already subscribed)
                            if (characteristic.CanUpdate && !_characteristicHandlerRegistered)
                            {
                                characteristic.ValueUpdated += OnCharacteristicValueUpdated;
                                _characteristicHandlerRegistered = true;
                                await characteristic.StartUpdatesAsync();
                            }
                        }
                    }
                }

                if (_writeCharacteristic != null && _readCharacteristic != null)
                {
                    foundCharacteristics = true;
                    break;
                }
            }

            // Fallback: use same characteristic for read/write if only one found
            if (_writeCharacteristic != null && _readCharacteristic == null)
            {
                _readCharacteristic = _writeCharacteristic;
                if (_readCharacteristic.CanUpdate && !_characteristicHandlerRegistered)
                {
                    _readCharacteristic.ValueUpdated += OnCharacteristicValueUpdated;
                    _characteristicHandlerRegistered = true;
                    await _readCharacteristic.StartUpdatesAsync();
                }
                foundCharacteristics = true;
            }
            else if (_readCharacteristic != null && _writeCharacteristic == null)
            {
                _writeCharacteristic = _readCharacteristic;
                foundCharacteristics = true;
            }

            if (!foundCharacteristics)
            {
                ErrorOccurred?.Invoke(this, "Could not find OBD characteristics");
                await DisconnectAsync();
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                return false;
            }

            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connected);
            return true;
        }
        catch (DeviceConnectionException ex)
        {
            ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Connection error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnecting);

            // Unsubscribe from characteristic events
            if (_readCharacteristic != null && _characteristicHandlerRegistered)
            {
                _readCharacteristic.ValueUpdated -= OnCharacteristicValueUpdated;
                _characteristicHandlerRegistered = false;

                if (_readCharacteristic.CanUpdate)
                {
                    try
                    {
                        await _readCharacteristic.StopUpdatesAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error stopping updates: {ex.Message}");
                    }
                }
            }

            IDevice? deviceToDisconnect;
            lock (_lock)
            {
                deviceToDisconnect = _connectedDevice;
                _connectedDevice = null;
                _writeCharacteristic = null;
                _readCharacteristic = null;
            }

            if (deviceToDisconnect != null)
            {
                await _adapter.DisconnectDeviceAsync(deviceToDisconnect);
            }

            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Disconnect error: {ex.Message}");
            lock (_lock)
            {
                _connectedDevice = null;
                _writeCharacteristic = null;
                _readCharacteristic = null;
            }
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
        }
    }

    public async Task<bool> WriteAsync(byte[] data)
    {
        if (_disposed) return false;

        ICharacteristic? writeChar;
        lock (_lock)
        {
            writeChar = _writeCharacteristic;
            if (writeChar == null || _connectedDevice?.State != DeviceState.Connected)
            {
                ErrorOccurred?.Invoke(this, "Not connected or no write characteristic");
                return false;
            }
        }

        // Validate data length (BLE MTU typically 20 bytes for write)
        if (data == null || data.Length == 0)
        {
            ErrorOccurred?.Invoke(this, "No data to write");
            return false;
        }

        try
        {
            await writeChar.WriteAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Write failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> WriteAsync(string data)
    {
        if (string.IsNullOrEmpty(data)) return false;

        // Add carriage return if not present (required by ELM327)
        if (!data.EndsWith("\r"))
        {
            data += "\r";
        }

        return await WriteAsync(Encoding.ASCII.GetBytes(data));
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        if (_disposed) return;

        var device = new BluetoothDevice
        {
            Id = e.Device.Id.ToString(),
            Name = e.Device.Name ?? "Unknown",
            Address = e.Device.Id.ToString(),
            Rssi = e.Device.Rssi,
            DeviceType = BluetoothDeviceType.BLE,
            NativeDevice = e.Device
        };

        DeviceDiscovered?.Invoke(this, device);
    }

    private void OnDeviceConnected(object? sender, DeviceEventArgs e)
    {
        if (_disposed) return;
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connected);
    }

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _connectedDevice = null;
            _writeCharacteristic = null;
            _readCharacteristic = null;
        }
        _characteristicHandlerRegistered = false;
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _connectedDevice = null;
            _writeCharacteristic = null;
            _readCharacteristic = null;
        }
        _characteristicHandlerRegistered = false;
        ErrorOccurred?.Invoke(this, $"Connection lost: {e.ErrorMessage}");
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
    }

    private void OnCharacteristicValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var value = e.Characteristic?.Value;
            if (value != null && value.Length > 0)
            {
                DataReceived?.Invoke(this, value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in characteristic update: {ex.Message}");
        }
    }

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
            // Unsubscribe from adapter events
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceConnected -= OnDeviceConnected;
            _adapter.DeviceDisconnected -= OnDeviceDisconnected;
            _adapter.DeviceConnectionLost -= OnDeviceConnectionLost;

            // Unsubscribe from characteristic events
            if (_readCharacteristic != null && _characteristicHandlerRegistered)
            {
                _readCharacteristic.ValueUpdated -= OnCharacteristicValueUpdated;
                _characteristicHandlerRegistered = false;
            }

            // Disconnect if still connected
            lock (_lock)
            {
                _connectedDevice = null;
                _writeCharacteristic = null;
                _readCharacteristic = null;
            }
        }

        _disposed = true;
    }

    ~BleService()
    {
        Dispose(false);
    }
}
