namespace VigiaHud.Core.Device;

/// <summary>
/// How much a photo must turn to stand upright, from where gravity pulls at the moment it is taken (DECISIONS 022):
/// the owner may stand the phone upright or lay it on either side, and the photo should look right in every case.
/// The camera writes the turn into the JPEG and Telegram honours it.
/// </summary>
public static class PhotoOrientation
{
    /// <summary>
    /// The phone's rotation from upright portrait, clockwise, to the nearest quarter (0, 90, 180, 270), from one
    /// accelerometer reading in Android's axes (x to the right of the screen, y to its top, z out of it; at rest the
    /// reading points up). Null when the phone lies flat, face up or down: then gravity says nothing about which side
    /// is up. Same rule as Android's <c>OrientationEventListener</c>: flat when the tilt is under about 27 degrees.
    /// </summary>
    public static int? DeviceRotation(double x, double y, double z)
    {
        var inPlane = x * x + y * y;
        if (inPlane * 4 < z * z || inPlane == 0)
        {
            return null;
        }

        var degrees = 90 - (int)Math.Round(Math.Atan2(y, -x) * 180 / Math.PI);
        var normalized = ((degrees % 360) + 360) % 360;
        return (normalized + 45) / 90 * 90 % 360;
    }

    /// <summary>
    /// The turn to write into the JPEG of the back camera: the sensor's mounting plus the phone's rotation.
    /// </summary>
    /// <param name="sensorOrientation">The camera's <c>SENSOR_ORIENTATION</c> (90 on the OPPO).</param>
    /// <param name="deviceRotation">From <see cref="DeviceRotation"/>.</param>
    public static int JpegOrientation(int sensorOrientation, int deviceRotation) =>
        (((sensorOrientation + deviceRotation) % 360) + 360) % 360;
}
