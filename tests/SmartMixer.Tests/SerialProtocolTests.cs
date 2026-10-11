using SmartMixer.Core;
using Xunit;

namespace SmartMixer.Tests;

public sealed class SerialProtocolTests
{
    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(true, 1, 0)]
    public void CalibrationMapsPhysicalEndpointsToConfiguredVolume(bool inverted, double expectedAtMinimum, double expectedAtMaximum)
    {
        var calibration = new PotentiometerCalibration
        {
            RawMinimum = 120,
            RawMaximum = 900,
            DeadzonePercent = 0,
            Sensitivity = 1,
            Inverted = inverted
        };

        Assert.Equal(expectedAtMinimum, calibration.Normalize(120));
        Assert.Equal(expectedAtMaximum, calibration.Normalize(900));
        Assert.Equal(0.5, calibration.Normalize(510), 3);
    }

    [Fact]
    public void CalibrationClampsReadingsOutsideCapturedEndpoints()
    {
        var calibration = new PotentiometerCalibration { RawMinimum = 200, RawMaximum = 800, DeadzonePercent = 0 };

        Assert.Equal(0, calibration.Normalize(0));
        Assert.Equal(1, calibration.Normalize(1023));
    }

    [Fact]
    public void ParsesSevenReadingsAndTouchState()
    {
        var valid = SerialProtocol.TryParseDataPacket("D,42,0,1,512,1023,4,5,6,1", out var sequence, out var values, out var touch);

        Assert.True(valid);
        Assert.Equal(42u, sequence);
        Assert.Equal(new[] { 0, 1, 512, 1023, 4, 5, 6 }, values);
        Assert.True(touch);
    }

    [Theory]
    [InlineData("D,1,0,0,0,0,0,0,1024,0")]
    [InlineData("D,4294967296,0,0,0,0,0,0,0,0")]
    [InlineData("D,1,0,0,0,0,0,0,0,2")]
    [InlineData("D,1,0,0,0,0,0,0,0")]
    [InlineData("D,1,0,0,0,0,0,0,0,0,extra")]
    [InlineData("D,x,0,0,0,0,0,0,0,0")]
    public void RejectsMalformedOrOutOfRangeFrames(string frame)
    {
        Assert.False(SerialProtocol.TryParseDataPacket(frame, out _, out _, out _));
    }
}
