using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Tests;

public class LevelScaleTests
{
    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(0.1, -20)]
    [InlineData(0.001, -60)]
    public void ToDb_converts_linear_peaks(double peak, double expectedDb) =>
        Assert.Equal(expectedDb, LevelScale.ToDb(peak), precision: 9);

    [Fact]
    public void ToDb_of_silence_is_negative_infinity() =>
        Assert.Equal(double.NegativeInfinity, LevelScale.ToDb(0));

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.0001, 0)]      // −80 dB: below the floor, empty bar
    [InlineData(0.001, 0)]       // −60 dB: the floor
    [InlineData(0.1, 40 / 60.0)] // −20 dB
    [InlineData(1.0, 1)]
    [InlineData(2.0, 1)]         // over full scale: clamped
    public void ToMeter_maps_minus_60_to_0_dB_onto_the_bar(double peak, double expected) =>
        Assert.Equal(expected, LevelScale.ToMeter(peak), precision: 9);

    [Theory]
    [InlineData(-60, 0)]
    [InlineData(-30, 0.5)]
    [InlineData(0, 1)]
    [InlineData(-90, 0)]
    [InlineData(6, 1)]
    public void DbToMeter_is_clamped(double db, double expected) =>
        Assert.Equal(expected, LevelScale.DbToMeter(db), precision: 9);

    [Theory]
    [InlineData(0.0, "< -60 dBFS")]
    [InlineData(0.0001, "< -60 dBFS")]
    [InlineData(0.5, "-6.0 dBFS")]
    [InlineData(1.0, "0.0 dBFS")]
    public void Format_reads_dBFS_and_marks_the_floor(double peak, string expected) =>
        Assert.Equal(expected, LevelScale.Format(peak));
}
