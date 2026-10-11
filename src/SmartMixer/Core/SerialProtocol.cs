using System.Globalization;

namespace SmartMixer.Core;

public static class SerialProtocol
{
    public static bool TryParseDataPacket(string line, out uint sequence, out int[] values, out bool touch)
    {
        sequence = 0;
        values = [];
        touch = false;
        var parts = line.Split(',');
        if (parts.Length != HardwareConfig.ChannelCount + 3 || parts[0] != "D"
            || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            || !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var touchValue)
            || touchValue is < 0 or > 1)
            return false;

        var parsed = new int[HardwareConfig.ChannelCount];
        for (var i = 0; i < HardwareConfig.ChannelCount; i++)
        {
            if (!int.TryParse(parts[i + 2], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i])
                || parsed[i] is < 0 or > 1023)
                return false;
        }
        values = parsed;
        touch = touchValue == 1;
        return true;
    }
}
