namespace CursorMeter;

public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
