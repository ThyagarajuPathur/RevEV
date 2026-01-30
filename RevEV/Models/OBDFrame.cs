namespace RevEV.Models;

public class OBDFrame
{
    public DateTime Timestamp { get; set; }
    public OBDFrameDirection Direction { get; set; }
    public string RawData { get; set; } = string.Empty;
    public byte[]? Bytes { get; set; }
    public string? Pid { get; set; }
    public string? ParsedValue { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }

    public OBDFrame()
    {
        Timestamp = DateTime.UtcNow;
    }

    public static OBDFrame CreateTx(string data)
    {
        return new OBDFrame
        {
            Direction = OBDFrameDirection.Transmit,
            RawData = data,
            Bytes = ParseHexString(data)
        };
    }

    public static OBDFrame CreateRx(string data)
    {
        var frame = new OBDFrame
        {
            Direction = OBDFrameDirection.Receive,
            RawData = data,
            Bytes = ParseHexString(data)
        };

        // Try to extract PID from response
        if (frame.Bytes != null && frame.Bytes.Length >= 2 && frame.Bytes[0] == 0x41)
        {
            frame.Pid = frame.Bytes[1].ToString("X2");
        }

        return frame;
    }

    public static OBDFrame CreateError(string message)
    {
        return new OBDFrame
        {
            Direction = OBDFrameDirection.Receive,
            IsError = true,
            ErrorMessage = message,
            RawData = message
        };
    }

    private static byte[]? ParseHexString(string hex)
    {
        try
        {
            // Remove spaces and other whitespace
            hex = hex.Replace(" ", "").Replace("\r", "").Replace("\n", "").Trim();

            // Check for non-hex characters (like "OK", "?", etc.)
            if (hex.Any(c => !Uri.IsHexDigit(c)))
            {
                return null;
            }

            if (hex.Length % 2 != 0)
            {
                return null;
            }

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public string ToHexString()
    {
        if (Bytes == null) return RawData;
        return BitConverter.ToString(Bytes).Replace("-", " ");
    }

    public string GetDisplayText()
    {
        string prefix = Direction == OBDFrameDirection.Transmit ? "TX" : "RX";
        string content = IsError ? $"ERROR: {ErrorMessage}" : RawData;

        if (!string.IsNullOrEmpty(ParsedValue))
        {
            content += $" [{ParsedValue}]";
        }

        return $"[{Timestamp:HH:mm:ss.fff}] {prefix}: {content}";
    }
}

public enum OBDFrameDirection
{
    Transmit,
    Receive
}
