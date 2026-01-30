using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using RevEV.Models;

namespace RevEV.Services.Bluetooth;

public partial class OBDProtocolHandler : ObservableObject
{
    private readonly BluetoothManager _bluetoothManager;
    private readonly StringBuilder _responseBuffer = new();
    private TaskCompletionSource<string>? _responseTcs;
    private CancellationTokenSource? _pollingCts;
    private bool _isInitialized;

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

                // Check for error responses
                if (response == null || response.Contains("ERROR") || response.Contains("?"))
                {
                    // Continue anyway - some adapters don't support all commands
                    continue;
                }

                await Task.Delay(100); // Brief delay between commands
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
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _bluetoothManager.IsConnected)
            {
                try
                {
                    // Poll RPM
                    await PollRpmAsync();

                    // Small delay between PIDs
                    await Task.Delay(25, token);

                    // Poll Speed
                    await PollSpeedAsync();

                    // Notify data updated
                    DataUpdated?.Invoke(this, CurrentData);

                    // Target ~20Hz per value (40Hz total, with 25ms delay = ~40ms per cycle)
                    await Task.Delay(25, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Polling error: {ex.Message}");
                    await Task.Delay(500, token); // Back off on error
                }
            }

            IsPolling = false;
        }, token);
    }

    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
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
        if (!_bluetoothManager.IsConnected) return null;

        _responseBuffer.Clear();
        _responseTcs = new TaskCompletionSource<string>();

        // Log TX
        var txFrame = OBDFrame.CreateTx(command);
        FrameReceived?.Invoke(this, txFrame);

        // Send command
        var sent = await _bluetoothManager.WriteAsync(command);
        if (!sent)
        {
            return null;
        }

        // Wait for response with timeout
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() => _responseTcs?.TrySetResult(_responseBuffer.ToString()));

        try
        {
            return await _responseTcs.Task;
        }
        catch
        {
            return null;
        }
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        var text = Encoding.ASCII.GetString(data);
        _responseBuffer.Append(text);

        // Check if we have a complete response (ends with > prompt)
        var response = _responseBuffer.ToString();
        if (response.Contains('>'))
        {
            // Clean up the response
            response = response
                .Replace(">", "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            _responseTcs?.TrySetResult(response);
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
            // Clean and split response
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
        catch
        {
            // Parsing failed
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
        catch
        {
            // Parsing failed
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
        catch
        {
            // Parsing failed
        }

        return null;
    }

    /// <summary>
    /// Sends a raw command to the adapter for debugging.
    /// </summary>
    public async Task<string?> SendRawCommandAsync(string command)
    {
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
}
