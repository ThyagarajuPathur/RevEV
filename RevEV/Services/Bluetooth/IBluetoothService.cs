using RevEV.Models;

namespace RevEV.Services.Bluetooth;

public interface IBluetoothService
{
    BluetoothServiceType ServiceType { get; }
    bool IsConnected { get; }
    bool IsScanning { get; }

    event EventHandler<BluetoothDevice>? DeviceDiscovered;
    event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;
    event EventHandler<byte[]>? DataReceived;
    event EventHandler<string>? ErrorOccurred;

    Task<bool> InitializeAsync();
    Task StartScanningAsync(CancellationToken cancellationToken = default);
    Task StopScanningAsync();
    Task<bool> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<bool> WriteAsync(byte[] data);
    Task<bool> WriteAsync(string data);
}

public enum BluetoothServiceType
{
    BLE,
    Classic
}

public enum BluetoothConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Error
}

public class BluetoothDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Rssi { get; set; }
    public BluetoothDeviceType DeviceType { get; set; }
    public bool IsPaired { get; set; }
    public object? NativeDevice { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? Address : Name;

    public override string ToString() => $"{DisplayName} ({Address})";
}

public enum BluetoothDeviceType
{
    Unknown,
    BLE,
    Classic,
    Dual
}
