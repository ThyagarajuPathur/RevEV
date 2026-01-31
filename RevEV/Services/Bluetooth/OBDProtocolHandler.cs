using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using RevEV.Models;

namespace RevEV.Services.Bluetooth;

public partial class OBDProtocolHandler : ObservableObject, IDisposable
{
    private readonly BluetoothManager _bluetoothManager;
    private readonly object _lock = new();
    private readonly StringBuilder _responseBuffer = new();
    private TaskCompletionSource<string>? _responseTcs;
    private CancellationTokenSource? _pollingCts;
    private volatile bool _isInitialized;
    private bool _disposed;

    [ObservableProperty]
    private bool _isPolling;

    [ObservableProperty]
    private VehicleData _currentData = new();

    public event EventHandler<OBDFrame>? FrameReceived;
    public event EventHandler<VehicleData>? DataUpdated;
    public event EventHandler<string>? ErrorOccurred;

    // OBD-II PIDs
    private const string PID_RPM = "010C";
    private const string PID_SPEED = "010D";
    private const string PID_THROTTLE = "0111";

    // ELM327 AT Commands
    private static readonly string[] InitCommands = new[]
    {
        "ATZ",      // Reset
        "ATE0",     // Echo off
        "ATL0",     // Linefeeds off
        "ATH0",     // Headers off
        "ATSP0",    // Auto-detect protocol
        "ATCAF1",   // CAN formatting on
    };

    public OBDProtocolHandler(BluetoothManager bluetoothManager)
    {
        _bluetoothManager = bluetoothManager;
        _bluetoothManager.DataReceived += OnDataReceived;
    }

    public async Task<bool> InitializeAsync()
    {
        if (_disposed) return false;

        if (!_bluetoothManager.IsConnected)
        {
            ErrorOccurred?.Invoke(this, "Not connected to adapter");
            return false;
        }

        _isInitialized = false;

        try
        {
            foreach (var command in InitCommands)
            {
                var response = await SendCommandAsync(command, TimeSpan.FromSeconds(3));

                var frame = OBDFrame.CreateTx(command);
                FrameReceived?.Invoke(this, frame);

                if (response != null)
                {
                    var rxFrame = OBDFrame.CreateRx(response);
                    FrameReceived?.Invoke(this, rxFrame);
                }

                // Check for error responses but continue anyway - some adapters don't support all commands
                if (response == null || response.Contains("ERROR") || response.Contains("?"))
                {
                    continue;
                }

                await Task.Delay(100);
            }

            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Initialization failed: {ex.Message}");
            return false;
        }
    }

    public async Task StartPollingAsync()
    {
        if (_disposed) return;

        if (!_isInitialized)
        {
            var initResult = await InitializeAsync();
            if (!initResult)
            {
                ErrorOccurred?.Invoke(this, "Failed to initialize adapter");
                return;
            }
        }

        if (IsPolling) return;

        IsPolling = true;

        // Dispose previous CTS
        _pollingCts?.Dispose();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && _bluetoothManager.IsConnected)
                {
                    try
                    {
                        // Poll RPM
                        await PollRpmAsync();

                        if (token.IsCancellationRequested) break;

                        // Small delay between PIDs
                        await Task.Delay(25, token);

                        // Poll Speed
                        await PollSpeedAsync();

                        if (token.IsCancellationRequested) break;

                        // Notify data updated
                        DataUpdated?.Invoke(this, CurrentData);

                        await Task.Delay(25, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ErrorOccurred?.Invoke(this, $"Polling error: {ex.Message}");
                            try
                            {
                                await Task.Delay(500, token);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"Polling task error: {ex.Message}");
                }
            }
            finally
            {
                IsPolling = false;
            }
        }, token);
    }

    public void StopPolling()
    {
        var cts = _pollingCts;
        _pollingCts = null;

        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        IsPolling = false;
    }

    private async Task PollRpmAsync()
    {
        var response = await SendCommandAsync(PID_RPM, TimeSpan.FromMilliseconds(500));

        if (response != null)
        {
            var frame = OBDFrame.CreateRx(response);
            frame.Pid = "0C";
            var rpm = ParseRpmResponse(response);
            if (rpm.HasValue)
            {
                CurrentData.Rpm = rpm.Value;
                frame.ParsedValue = $"{rpm.Value:F0} RPM";
            }
            FrameReceived?.Invoke(this, frame);
        }
    }

    private async Task PollSpeedAsync()
    {
        var response = await SendCommandAsync(PID_SPEED, TimeSpan.FromMilliseconds(500));

        if (response != null)
        {
            var frame = OBDFrame.CreateRx(response);
            frame.Pid = "0D";
            var speed = ParseSpeedResponse(response);
            if (speed.HasValue)
            {
                CurrentData.Speed = speed.Value;
                frame.ParsedValue = $"{speed.Value:F0} km/h";
            }
            FrameReceived?.Invoke(this, frame);
        }
    }

    private async Task<string?> SendCommandAsync(string command, TimeSpan timeout)
    {
        if (_disposed) return null;
        if (!_bluetoothManager.IsConnected) return null;

        TaskCompletionSource<string> tcs;
        CancellationTokenSource cts;

        lock (_lock)
        {
            _responseBuffer.Clear();
            tcs = new TaskCompletionSource<string>();
            _responseTcs = tcs;
        }

        // Log TX
        var txFrame = OBDFrame.CreateTx(command);
        FrameReceived?.Invoke(this, txFrame);

        // Send command
        var sent = await _bluetoothManager.WriteAsync(command);
        if (!sent)
        {
            lock (_lock)
            {
                _responseTcs = null;
            }
            return null;
        }

        // Wait for response with timeout
        cts = new CancellationTokenSource(timeout);
        var registration = cts.Token.Register(() =>
        {
            lock (_lock)
            {
                if (_responseTcs == tcs)
                {
                    tcs.TrySetResult(_responseBuffer.ToString());
                }
            }
        });

        try
        {
            var result = await tcs.Task;
            return result;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            registration.Dispose();
            cts.Dispose();

            lock (_lock)
            {
                if (_responseTcs == tcs)
                {
                    _responseTcs = null;
                }
            }
        }
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        if (_disposed) return;

        try
        {
            var text = Encoding.ASCII.GetString(data);

            TaskCompletionSource<string>? tcs;
            string response;

            lock (_lock)
            {
                _responseBuffer.Append(text);
                response = _responseBuffer.ToString();
                tcs = _responseTcs;
            }

            // Check if we have a complete response (ends with > prompt)
            if (response.Contains('>') && tcs != null)
            {
                // Clean up the response
                response = response
                    .Replace(">", "")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

                tcs.TrySetResult(response);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data receive error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses RPM from OBD-II response.
    /// Response format: "41 0C XX YY" where RPM = ((XX * 256) + YY) / 4
    /// </summary>
    private static float? ParseRpmResponse(string response)
    {
        try
        {
            var parts = response.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Find "41 0C" pattern
            for (int i = 0; i < parts.Length - 3; i++)
            {
                if (parts[i] == "41" && parts[i + 1].Equals("0C", StringComparison.OrdinalIgnoreCase))
                {
                    byte a = Convert.ToByte(parts[i + 2], 16);
                    byte b = Convert.ToByte(parts[i + 3], 16);
                    return ((a * 256f) + b) / 4f;
                }
            }

            // Alternative: just try to parse last two hex values
            if (parts.Length >= 4)
            {
                byte a = Convert.ToByte(parts[^2], 16);
                byte b = Convert.ToByte(parts[^1], 16);
                return ((a * 256f) + b) / 4f;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RPM parse error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Parses speed from OBD-II response.
    /// Response format: "41 0D XX" where Speed = XX km/h
    /// </summary>
    private static float? ParseSpeedResponse(string response)
    {
        try
        {
            var parts = response.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Find "41 0D" pattern
            for (int i = 0; i < parts.Length - 2; i++)
            {
                if (parts[i] == "41" && parts[i + 1].Equals("0D", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToByte(parts[i + 2], 16);
                }
            }

            // Alternative: just try to parse last hex value
            if (parts.Length >= 3)
            {
                return Convert.ToByte(parts[^1], 16);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Speed parse error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Parses throttle position from OBD-II response.
    /// Response format: "41 11 XX" where Throttle = (XX * 100) / 255 %
    /// </summary>
    private static float? ParseThrottleResponse(string response)
    {
        try
        {
            var parts = response.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length - 2; i++)
            {
                if (parts[i] == "41" && parts[i + 1].Equals("11", StringComparison.OrdinalIgnoreCase))
                {
                    byte value = Convert.ToByte(parts[i + 2], 16);
                    return (value * 100f) / 255f;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Throttle parse error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Sends a raw command to the adapter for debugging.
    /// </summary>
    public async Task<string?> SendRawCommandAsync(string command)
    {
        if (_disposed) return null;

        var txFrame = OBDFrame.CreateTx(command);
        FrameReceived?.Invoke(this, txFrame);

        var response = await SendCommandAsync(command, TimeSpan.FromSeconds(2));

        if (response != null)
        {
            var rxFrame = OBDFrame.CreateRx(response);
            FrameReceived?.Invoke(this, rxFrame);
        }

        return response;
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
            StopPolling();

            _bluetoothManager.DataReceived -= OnDataReceived;

            lock (_lock)
            {
                _responseTcs?.TrySetCanceled();
                _responseTcs = null;
            }
        }

        _disposed = true;
    }

    ~OBDProtocolHandler()
    {
        Dispose(false);
    }
}
