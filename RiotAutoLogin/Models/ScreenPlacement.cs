namespace RiotAutoLogin.Models
{
    public readonly record struct PixelLocation(int X, int Y);

    public static class ScreenPlacement
    {
        // Both the monitor work area and the actual HWND bounds must use the
        // same (device pixel) coordinate system. Origins may be negative.
        public static PixelLocation Center(int workLeft, int workTop, int workWidth, int workHeight,
            int windowWidth, int windowHeight) => new(
            workLeft + (workWidth - windowWidth) / 2,
            workTop + (workHeight - windowHeight) / 2);
    }
}
