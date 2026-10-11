namespace SmartMixer.Core;

public static class HardwareConfig
{
    public const int ChannelCount = 7;
    public const int TouchDebounceMilliseconds = 300;
    public const int DefaultBaudRate = 115200;
    public static readonly string[] PotentiometerPins = ["A0", "A1", "A2", "A3", "A6", "A7", "A8"];
    public static readonly string[] LedPins = ["D2", "D3", "D5", "D7", "D14", "D15", "D16"];
    public const string TouchPin = "D9";
}
