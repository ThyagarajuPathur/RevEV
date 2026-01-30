using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevEV.Models;
using RevEV.Services.Bluetooth;

namespace RevEV.ViewModels;

public partial class TerminalViewModel : BaseViewModel
{
    private readonly OBDProtocolHandler _obdHandler;
    private readonly BluetoothManager _bluetoothManager;
    private const int MaxLogEntries = 500;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _commandInput = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    public ObservableCollection<OBDFrameDisplay> LogEntries { get; } = new();

    public TerminalViewModel(OBDProtocolHandler obdHandler, BluetoothManager bluetoothManager)
    {
        _obdHandler = obdHandler;
        _bluetoothManager = bluetoothManager;

        Title = "Terminal";

        _obdHandler.FrameReceived += OnFrameReceived;
        _bluetoothManager.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public void Initialize()
    {
        IsConnected = _bluetoothManager.IsConnected;
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandInput) || !IsConnected) return;

        var command = CommandInput.Trim().ToUpperInvariant();
        CommandInput = string.Empty;

        await _obdHandler.SendRawCommandAsync(command);
    }

    [RelayCommand]
    private async Task SendQuickCommandAsync(string command)
    {
        if (!IsConnected) return;
        await _obdHandler.SendRawCommandAsync(command);
    }

    private void OnFrameReceived(object? sender, OBDFrame frame)
    {
        if (IsPaused) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var display = new OBDFrameDisplay(frame);
            LogEntries.Add(display);

            // Trim old entries
            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, BluetoothConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = state == BluetoothConnectionState.Connected;
        });
    }

    public void Cleanup()
    {
        _obdHandler.FrameReceived -= OnFrameReceived;
        _bluetoothManager.ConnectionStateChanged -= OnConnectionStateChanged;
    }
}

/// <summary>
/// Display wrapper for OBDFrame with formatting properties for the UI.
/// </summary>
public class OBDFrameDisplay
{
    public string Timestamp { get; }
    public string Direction { get; }
    public string Content { get; }
    public Color TextColor { get; }
    public bool IsError { get; }

    public OBDFrameDisplay(OBDFrame frame)
    {
        Timestamp = frame.Timestamp.ToString("HH:mm:ss.fff");
        Direction = frame.Direction == OBDFrameDirection.Transmit ? "TX" : "RX";
        IsError = frame.IsError;

        if (frame.IsError)
        {
            Content = $"ERROR: {frame.ErrorMessage}";
            TextColor = Color.FromArgb("#FF3366"); // Warning Red
        }
        else if (frame.Direction == OBDFrameDirection.Transmit)
        {
            Content = frame.RawData;
            TextColor = Color.FromArgb("#00FF41"); // Terminal Green
        }
        else
        {
            Content = string.IsNullOrEmpty(frame.ParsedValue)
                ? frame.RawData
                : $"{frame.RawData} [{frame.ParsedValue}]";
            TextColor = Color.FromArgb("#00FFFF"); // Neon Cyan
        }
    }
}
