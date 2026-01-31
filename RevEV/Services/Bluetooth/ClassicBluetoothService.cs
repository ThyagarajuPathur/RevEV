using System.Text;

namespace RevEV.Services.Bluetooth;

/// <summary>
/// Classic Bluetooth SPP service for older ELM327 adapters.
/// Note: Classic Bluetooth is only available on Android.
/// On iOS, this service will report as unavailable.
/// </summary>
public class ClassicBluetoothService : IBluetoothService, IDisposable
{
    private readonly object _lock = new();
    private volatile bool _isInitialized;
    private volatile bool _isConnected;
    private volatile bool _isScanning;
    private bool _permissionsGranted;
    private bool _disposed;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _discoveryCts;
    private bool _showAllDevices = true;

#if ANDROID
    private Android.Bluetooth.BluetoothAdapter? _adapter;
    private Android.Bluetooth.BluetoothSocket? _socket;
    private Java.Util.UUID? _sppUuid;
    private DiscoveryReceiver? _discoveryReceiver;
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

    public void SetShowAllDevices(bool showAll)
    {
        _showAllDevices = showAll;
    }

    public async Task<bool> InitializeAsync()
    {
        if (_disposed) return false;
        if (_isInitialized && _permissionsGranted) return true;

#if ANDROID
        try
        {
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
                // Android < 12 requires Location for Bluetooth scanning
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

            _adapter = Android.Bluetooth.BluetoothAdapter.DefaultAdapter;
            if (_adapter == null)
            {
                ErrorOccurred?.Invoke(this, "Bluetooth not supported on this device");
                return false;
            }

            if (!_adapter.IsEnabled)
            {
                ErrorOccurred?.Invoke(this, "Please turn on Bluetooth to scan for devices");
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
        if (_disposed) return;

#if ANDROID
        if (!_isInitialized || !_permissionsGranted)
        {
            var initResult = await InitializeAsync();
            if (!initResult) return;
        }

        if (_adapter == null) return;

        // Re-check Bluetooth state
        if (!_adapter.IsEnabled)
        {
            ErrorOccurred?.Invoke(this, "Please turn on Bluetooth to scan for devices");
            return;
        }

        // Dispose previous CTS if any
        _discoveryCts?.Dispose();

        try
        {
            _isScanning = true;
            _discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // First, report ALL paired/bonded devices
            var pairedDevices = _adapter.BondedDevices;
            if (pairedDevices != null)
            {
                foreach (var device in pairedDevices)
                {
                    if (device == null) continue;

                    bool includeDevice = _showAllDevices ||
                        (device.Name != null && IsLikelyOBDDevice(device.Name));

                    if (includeDevice)
                    {
                        var btDevice = new BluetoothDevice
                        {
                            Id = device.Address ?? string.Empty,
                            Name = device.Name ?? "Unknown Device",
                            Address = device.Address ?? string.Empty,
                            DeviceType = BluetoothDeviceType.Classic,
                            IsPaired = true,
                            NativeDevice = device
                        };
                        DeviceDiscovered?.Invoke(this, btDevice);
                    }
                }
            }

            // Start actual discovery for unpaired devices
            _discoveryReceiver = new DiscoveryReceiver(this, _showAllDevices);
            var filter = new Android.Content.IntentFilter();
            filter.AddAction(Android.Bluetooth.BluetoothDevice.ActionFound);
            filter.AddAction(Android.Bluetooth.BluetoothAdapter.ActionDiscoveryFinished);

            var context = Android.App.Application.Context;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                context.RegisterReceiver(_discoveryReceiver, filter, Android.Content.ReceiverFlags.NotExported);
            }
            else
            {
#pragma warning disable CA1422
                context.RegisterReceiver(_discoveryReceiver, filter);
#pragma warning restore CA1422
            }

            // Cancel any existing discovery and start new one
            if (_adapter.IsDiscovering)
            {
                _adapter.CancelDiscovery();
            }

            var started = _adapter.StartDiscovery();
            if (!started)
            {
                ErrorOccurred?.Invoke(this, "Failed to start Bluetooth discovery. Make sure Bluetooth is enabled.");
            }

            // Wait for discovery to complete or cancellation
            try
            {
                await Task.Delay(12000, _discoveryCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Stop discovery if still running
            if (_adapter.IsDiscovering)
            {
                _adapter.CancelDiscovery();
            }
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
            UnregisterDiscoveryReceiver();
        }
#else
        await Task.CompletedTask;
        ErrorOccurred?.Invoke(this, "Classic Bluetooth scanning not available on this platform");
#endif
    }

#if ANDROID
    private void UnregisterDiscoveryReceiver()
    {
        try
        {
            if (_discoveryReceiver != null)
            {
                Android.App.Application.Context.UnregisterReceiver(_discoveryReceiver);
                _discoveryReceiver = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error unregistering receiver: {ex.Message}");
        }
    }
#endif

    private static bool IsLikelyOBDDevice(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("OBD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ELM", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("VLINK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("V-LINK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("iCar", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("VEEPEAK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("OBDII", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("OBD2", StringComparison.OrdinalIgnoreCase);
    }

    public Task StopScanningAsync()
    {
        _isScanning = false;
        _discoveryCts?.Cancel();
#if ANDROID
        _adapter?.CancelDiscovery();
        UnregisterDiscoveryReceiver();
#endif
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

#if ANDROID
        // Validate device BEFORE changing state
        if (device.NativeDevice is not Android.Bluetooth.BluetoothDevice nativeDevice)
        {
            ErrorOccurred?.Invoke(this, "Invalid device reference");
            return false;
        }

        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Connecting);

            // Cancel any ongoing discovery
            _adapter?.CancelDiscovery();

            // Create RFCOMM socket
            var socket = nativeDevice.CreateRfcommSocketToServiceRecord(_sppUuid);

            if (socket == null)
            {
                ErrorOccurred?.Invoke(this, "Failed to create Bluetooth socket");
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                return false;
            }

            lock (_lock)
            {
                _socket = socket;
            }

            // Connect with timeout
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));

            await Task.Run(() => socket.Connect(), connectCts.Token);

            if (!socket.IsConnected)
            {
                ErrorOccurred?.Invoke(this, "Socket connection failed");
                ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
                CleanupSocket();
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
            CleanupSocket();
            return false;
        }
        catch (Java.IO.IOException ex)
        {
            ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
            CleanupSocket();
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Connection error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Error);
            CleanupSocket();
            return false;
        }
#else
        await Task.CompletedTask;
        ErrorOccurred?.Invoke(this, "Classic Bluetooth not available on this platform");
        return false;
#endif
    }

#if ANDROID
    private void CleanupSocket()
    {
        lock (_lock)
        {
            if (_socket != null)
            {
                try
                {
                    _socket.Close();
                    _socket.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Socket cleanup error: {ex.Message}");
                }
                _socket = null;
            }
        }
    }
#endif

    public async Task DisconnectAsync()
    {
#if ANDROID
        try
        {
            ConnectionStateChanged?.Invoke(this, BluetoothConnectionState.Disconnecting);

            // Cancel read operation
            _readCts?.Cancel();

            Android.Bluetooth.BluetoothSocket? socketToClose;
            lock (_lock)
            {
                socketToClose = _socket;
                _socket = null;
            }

            if (socketToClose != null)
            {
                try
                {
                    socketToClose.InputStream?.Close();
                    socketToClose.OutputStream?.Close();
                    socketToClose.Close();
                    socketToClose.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Socket close error: {ex.Message}");
                }
            }

            // Dispose CTS after cancellation
            _readCts?.Dispose();
            _readCts = null;

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
        if (_disposed) return false;

#if ANDROID
        Android.Bluetooth.BluetoothSocket? socket;
        lock (_lock)
        {
            socket = _socket;
        }

        if (socket?.OutputStream == null || !_isConnected)
        {
            ErrorOccurred?.Invoke(this, "Not connected");
            return false;
        }

        if (data == null || data.Length == 0)
        {
            ErrorOccurred?.Invoke(this, "No data to write");
            return false;
        }

        try
        {
            await socket.OutputStream.WriteAsync(data);
            await socket.OutputStream.FlushAsync();
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
        if (string.IsNullOrEmpty(data)) return false;

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
        _readCts?.Dispose();
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;

        Task.Run(async () =>
        {
            byte[] buffer = new byte[1024];

            try
            {
                while (!token.IsCancellationRequested && _isConnected)
                {
                    Android.Bluetooth.BluetoothSocket? socket;
                    lock (_lock)
                    {
                        socket = _socket;
                    }

                    if (socket?.InputStream == null) break;

                    try
                    {
                        int bytesRead = await socket.InputStream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead > 0)
                        {
                            byte[] data = new byte[bytesRead];
                            Array.Copy(buffer, data, bytesRead);
                            DataReceived?.Invoke(this, data);
                        }
                        else if (bytesRead == 0)
                        {
                            // Stream closed
                            break;
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
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && _isConnected)
                {
                    ErrorOccurred?.Invoke(this, $"Read error: {ex.Message}");
                }
            }
        }, token);
    }

    internal void OnDeviceFound(BluetoothDevice device)
    {
        if (_disposed) return;
        DeviceDiscovered?.Invoke(this, device);
    }
#endif

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
            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = null;

            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            _discoveryCts = null;

#if ANDROID
            UnregisterDiscoveryReceiver();
            CleanupSocket();
#endif
        }

        _isConnected = false;
        _disposed = true;
    }

    ~ClassicBluetoothService()
    {
        Dispose(false);
    }
}

#if ANDROID
/// <summary>
/// BroadcastReceiver for handling Bluetooth device discovery
/// </summary>
internal class DiscoveryReceiver : Android.Content.BroadcastReceiver
{
    private readonly ClassicBluetoothService _service;
    private readonly bool _showAllDevices;
    private readonly HashSet<string> _discoveredAddresses = new();

    public DiscoveryReceiver(ClassicBluetoothService service, bool showAllDevices)
    {
        _service = service;
        _showAllDevices = showAllDevices;
    }

    public override void OnReceive(Android.Content.Context? context, Android.Content.Intent? intent)
    {
        if (intent == null) return;

        var action = intent.Action;

        if (action == Android.Bluetooth.BluetoothDevice.ActionFound)
        {
            Android.Bluetooth.BluetoothDevice? device = null;

            try
            {
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
                {
                    device = intent.GetParcelableExtra(
                        Android.Bluetooth.BluetoothDevice.ExtraDevice,
                        Java.Lang.Class.FromType(typeof(Android.Bluetooth.BluetoothDevice))) as Android.Bluetooth.BluetoothDevice;
                }
                else
                {
#pragma warning disable CA1422
                    device = intent.GetParcelableExtra(Android.Bluetooth.BluetoothDevice.ExtraDevice) as Android.Bluetooth.BluetoothDevice;
#pragma warning restore CA1422
                }

                if (device != null && !string.IsNullOrEmpty(device.Address))
                {
                    // Avoid duplicates (thread-safe check)
                    lock (_discoveredAddresses)
                    {
                        if (_discoveredAddresses.Contains(device.Address)) return;
                        _discoveredAddresses.Add(device.Address);
                    }

                    // Filter if not showing all devices
                    if (!_showAllDevices && !string.IsNullOrEmpty(device.Name) && !IsLikelyOBDDevice(device.Name))
                    {
                        return;
                    }

                    var btDevice = new BluetoothDevice
                    {
                        Id = device.Address,
                        Name = device.Name ?? "Unknown Device",
                        Address = device.Address,
                        DeviceType = BluetoothDeviceType.Classic,
                        IsPaired = device.BondState == Android.Bluetooth.Bond.Bonded,
                        NativeDevice = device
                    };

                    _service.OnDeviceFound(btDevice);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing discovered device: {ex.Message}");
            }
        }
    }

    private static bool IsLikelyOBDDevice(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("OBD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ELM", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("VLINK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("V-LINK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("iCar", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("VEEPEAK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("OBDII", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("OBD2", StringComparison.OrdinalIgnoreCase);
    }
}
#endif
