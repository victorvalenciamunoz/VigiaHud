using VigiaHud.Core.Device;

namespace VigiaHud.Core.Tests.Device;

public class PhotoOrientationTests
{
    private const double G = 9.81;

    [Theory]
    [InlineData(0, G, 0, 0)]            // standing upright
    [InlineData(-G, 0, 0, 90)]          // on its right side: top of the phone to the right
    [InlineData(0, -G, 0, 180)]         // upside down
    [InlineData(G, 0, 0, 270)]          // on its left side: top of the phone to the left
    [InlineData(1.5, 9.6, 1.0, 0)]      // a little crooked still counts as upright
    [InlineData(-6.9, 6.9, 0, 90)]      // at 45 degrees exactly, a tie: it goes to the next quarter clockwise
    [InlineData(-7.5, 6.0, 2.0, 90)]    // leaning more to the right side than upright
    [InlineData(0, 4.9, 8.5, 0)]        // tilted back 60 degrees, still clearly upright
    public void Gravity_gives_the_phones_rotation(double x, double y, double z, int expected) =>
        Assert.Equal(expected, PhotoOrientation.DeviceRotation(x, y, z));

    [Theory]
    [InlineData(0, 0, G)]         // face up on a table
    [InlineData(0, 0, -G)]        // face down
    [InlineData(0.5, 0.5, 9.7)]   // almost flat
    [InlineData(0, 0, 0)]         // no reading
    public void Flat_says_nothing_about_which_side_is_up(double x, double y, double z) =>
        Assert.Null(PhotoOrientation.DeviceRotation(x, y, z));

    [Theory]
    [InlineData(90, 0, 90)]     // upright: the OPPO's sensor turns 90
    [InlineData(90, 270, 0)]    // on its left side: already upright, as the author saw on 2026-11-03
    [InlineData(90, 90, 180)]   // on its right side: upside down unless turned
    [InlineData(90, 180, 270)]
    [InlineData(270, 0, 270)]
    public void The_jpeg_turn_adds_the_sensor_mounting_and_the_phones_rotation(int sensor, int rotation, int expected) =>
        Assert.Equal(expected, PhotoOrientation.JpegOrientation(sensor, rotation));
}
