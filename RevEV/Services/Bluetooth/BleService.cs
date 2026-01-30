using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using System.Text;

namespace RevEV.Services.Bluetooth;

public class BleService : IBluetoothService
{
    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;
    private IDevice? _connectedDevice;
    private ICharacteristic? _writeCharacteristic;
    private ICharacteristic? _readCharacteristic;
    private bool _isInitialized;

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
    public bool IsConnected => _connectedDevice?.State == DeviceState.Connected;
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
        if (_isInitialized) return true;

        try
        {
            // Check Bluetooth state
            if (_ble.State != BluetoothState.On)
            {
                ErrorOccurred?.Invoke(this, "Bluetooth is not enabled");
                return false;
            }

            // Request permissions on Android
#if ANDROID
            var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Bluetooth>();
                if (status != PermissionStatus.Granted)
                {
                    ErrorOccurred?.Invoke(this, "Bluetooth permission denied");
                    return false;
                }
            }

            var locationStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (locationStatus != PermissionStatus.Granted)
            {
                locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                if (locationStatus != PermissionStatus.Granted)
                {
                    ErrorOccurred?.Invoke(this, "Location permission denied (required for BLE scanning)");
                    return false;
                }
            }
#endif

            _adapter.ScanTimeout = 30000; // 30 seconds
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
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }

        try
        {
            await _adapter.StartScanningForDevicesAsync(
                serviceUuids: null, // Scan for all devices
                deviceFilter: device =>
                    !string.IsNullOrEmpty(device.Name) &&
                    (device.Name.Contains("OBD", StringComparison.OrdinalIgnoreCase) ||
                     device.Name.Contains("ELM", StringComparison.OrdinalIgnoreCase) ||
                     device.Name.Contains("VLINK", StringComparison.OrdinalIgnoreCase) ||
                     device.Name.Contains("iCar", StringComparison.OrdinalIgnoreCase) ||
                     device.Name.Contains("VEEPEAK", StringComparison.OrdinalIgnoreCase)),
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

    public async Task StopScanningAsync()
    {
        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }
    }

    public async Task<bool> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connecting);

            if (device.NativeDevice is not IDevice nativeDevice)
            {
                ErrorOccurred?.Invoke(this, "Invalid device reference");
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                return false;
            }

            var connectParams = new ConnectParameters(
                autoConnect: false,
                forceBleTransport: true);

            await _adapter.ConnectToDeviceAsync(nativeDevice, connectParams, cancellationToken);
            _connectedDevice = nativeDevice;

            // Discover services and characteristics
            var services = await _connectedDevice.GetServicesAsync();
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

                            // Subscribe to notifications
                            if (characteristic.CanUpdate)
                            {
                                characteristic.ValueUpdated += OnCharacteristicValueUpdated;
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
                if (_readCharacteristic.CanUpdate)
                {
                    _readCharacteristic.ValueUpdated += OnCharacteristicValueUpdated;
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

            if (_readCharacteristic != null)
            {
                _readCharacteristic.ValueUpdated -= OnCharacteristicValueUpdated;
                if (_readCharacteristic.CanUpdate)
                {
                    try
                    {
                        await _readCharacteristic.StopUpdatesAsync();
                    }
                    catch { /* Ignore errors during cleanup */ }
                }
            }

            if (_connectedDevice != null)
            {
                await _adapter.DisconnectDeviceAsync(_connectedDevice);
            }

            _connectedDevice = null;
            _writeCharacteristic = null;
            _readCharacteristic = null;

            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Disconnect error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
        }
    }

    public async Task<bool> WriteAsync(byte[] data)
    {
        if (_writeCharacteristic == null || !IsConnected)
        {
            ErrorOccurred?.Invoke(this, "Not connected or no write characteristic");
            return false;
        }

        try
        {
            await _writeCharacteristic.WriteAsync(data);
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
        // Add carriage return if not present (required by ELM327)
        if (!data.EndsWith("\r"))
        {
            data += "\r";
        }

        return await WriteAsync(Encoding.ASCII.GetBytes(data));
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
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
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connected);
    }

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        _connectedDevice = null;
        _writeCharacteristic = null;
        _readCharacteristic = null;
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        _connectedDevice = null;
        _writeCharacteristic = null;
        _readCharacteristic = null;
        ErrorOccurred?.Invoke(this, $"Connection lost: {e.ErrorMessage}");
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
    }

    private void OnCharacteristicValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        if (e.Characteristic.Value != null && e.Characteristic.Value.Length > 0)
        {
            DataReceived?.Invoke(this, e.Characteristic.Value);
        }
    }
}
