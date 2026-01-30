using System.Text;

namespace RevEV.Services.Bluetooth;

/// <summary>
/// Classic Bluetooth SPP service for older ELM327 adapters.
/// Note: Classic Bluetooth is only available on Android.
/// On iOS, this service will report as unavailable.
/// </summary>
public class ClassicBluetoothService : IBluetoothService
{
    private bool _isInitialized;
    private bool _isConnected;
    private bool _isScanning;
    private CancellationTokenSource? _readCts;

#if ANDROID
    private Android.Bluetooth.BluetoothAdapter? _adapter;
    private Android.Bluetooth.BluetoothSocket? _socket;
    private Java.Util.UUID? _sppUuid;
#endif

    // Standard SPP UUID
    private static readonly Guid SppUuid = Guid.Parse("00001101-0000-1000-8000-00805F9B34FB");

    public BluetoothServiceType ServiceType => BluetoothServiceType.Classic;
    public bool IsConnected => _isConnected;
    public bool IsScanning => _isScanning;

    public event EventHandler<BluetoothDevice>? DeviceDiscovered;
    public event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;
    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;

    public ClassicBluetoothService()
    {
#if ANDROID
        _sppUuid = Java.Util.UUID.FromString(SppUuid.ToString());
#endif
    }

    public async Task<bool> InitializeAsync()
    {
        if (_isInitialized) return true;

#if ANDROID
        try
        {
            // Request permissions
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

            _adapter = Android.Bluetooth.BluetoothAdapter.DefaultAdapter;
            if (_adapter == null)
            {
                ErrorOccurred?.Invoke(this, "Bluetooth not supported on this device");
                return false;
            }

            if (!_adapter.IsEnabled)
            {
                ErrorOccurred?.Invoke(this, "Bluetooth is not enabled");
                return false;
            }

            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Initialization failed: {ex.Message}");
            return false;
        }
#else
        // Classic Bluetooth is not available on iOS
        await Task.CompletedTask;
        ErrorOccurred?.Invoke(this, "Classic Bluetooth is not available on this platform");
        return false;
#endif
    }

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (_adapter == null) return;

        try
        {
            _isScanning = true;

            // Get paired devices first (most ELM327 adapters require pairing)
            var pairedDevices = _adapter.BondedDevices;
            if (pairedDevices != null)
            {
                foreach (var device in pairedDevices)
                {
                    if (device.Name != null &&
                        (device.Name.Contains("OBD", StringComparison.OrdinalIgnoreCase) ||
                         device.Name.Contains("ELM", StringComparison.OrdinalIgnoreCase) ||
                         device.Name.Contains("VLINK", StringComparison.OrdinalIgnoreCase)))
                    {
                        var btDevice = new BluetoothDevice
                        {
                            Id = device.Address ?? string.Empty,
                            Name = device.Name ?? "Unknown",
                            Address = device.Address ?? string.Empty,
                            DeviceType = BluetoothDeviceType.Classic,
                            IsPaired = true,
                            NativeDevice = device
                        };

                        DeviceDiscovered?.Invoke(this, btDevice);
                    }
                }
            }

            // Discovery for unpaired devices
            // Note: Discovery requires additional permissions and can be slow
            // Most users will have their OBD adapters already paired

            await Task.Delay(1000, cancellationToken); // Brief delay for UI update
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Scanning failed: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
        }
#else
        await Task.CompletedTask;
        ErrorOccurred?.Invoke(this, "Classic Bluetooth scanning not available on this platform");
#endif
    }

    public Task StopScanningAsync()
    {
        _isScanning = false;
#if ANDROID
        _adapter?.CancelDiscovery();
#endif
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
#if ANDROID
        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connecting);

            if (device.NativeDevice is not Android.Bluetooth.BluetoothDevice nativeDevice)
            {
                ErrorOccurred?.Invoke(this, "Invalid device reference");
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                return false;
            }

            // Cancel any ongoing discovery
            _adapter?.CancelDiscovery();

            // Create RFCOMM socket
            _socket = nativeDevice.CreateRfcommSocketToServiceRecord(_sppUuid);

            if (_socket == null)
            {
                ErrorOccurred?.Invoke(this, "Failed to create Bluetooth socket");
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                return false;
            }

            // Connect with timeout
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));

            await Task.Run(() => _socket.Connect(), connectCts.Token);

            if (!_socket.IsConnected)
            {
                ErrorOccurred?.Invoke(this, "Socket connection failed");
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                return false;
            }

            _isConnected = true;
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connected);

            // Start reading data
            StartReadingData();

            return true;
        }
        catch (OperationCanceledException)
        {
            ErrorOccurred?.Invoke(this, "Connection timed out");
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
            return false;
        }
        catch (Java.IO.IOException ex)
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
#else
        await Task.CompletedTask;
        ErrorOccurred?.Invoke(this, "Classic Bluetooth not available on this platform");
        return false;
#endif
    }

    public async Task DisconnectAsync()
    {
#if ANDROID
        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnecting);

            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = null;

            if (_socket != null)
            {
                try
                {
                    _socket.InputStream?.Close();
                    _socket.OutputStream?.Close();
                    _socket.Close();
                }
                catch { /* Ignore cleanup errors */ }

                _socket.Dispose();
                _socket = null;
            }

            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Disconnect error: {ex.Message}");
            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
        }
#else
        await Task.CompletedTask;
        _isConnected = false;
        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
#endif
    }

    public async Task<bool> WriteAsync(byte[] data)
    {
#if ANDROID
        if (_socket?.OutputStream == null || !_isConnected)
        {
            ErrorOccurred?.Invoke(this, "Not connected");
            return false;
        }

        try
        {
            await _socket.OutputStream.WriteAsync(data);
            await _socket.OutputStream.FlushAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Write failed: {ex.Message}");
            return false;
        }
#else
        await Task.CompletedTask;
        ErrorOccurred?.Invoke(this, "Classic Bluetooth not available on this platform");
        return false;
#endif
    }

    public async Task<bool> WriteAsync(string data)
    {
        // Add carriage return if not present
        if (!data.EndsWith("\r"))
        {
            data += "\r";
        }

        return await WriteAsync(Encoding.ASCII.GetBytes(data));
    }

#if ANDROID
    private void StartReadingData()
    {
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;

        Task.Run(async () =>
        {
            byte[] buffer = new byte[1024];

            while (!token.IsCancellationRequested && _socket?.InputStream != null && _isConnected)
            {
                try
                {
                    int bytesRead = await _socket.InputStream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead > 0)
                    {
                        byte[] data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                        DataReceived?.Invoke(this, data);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Java.IO.IOException)
                {
                    // Connection lost
                    if (_isConnected)
                    {
                        _isConnected = false;
                        ErrorOccurred?.Invoke(this, "Connection lost");
                        ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnected);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Read error: {ex.Message}");
                }
            }
        }, token);
    }
#endif
}
