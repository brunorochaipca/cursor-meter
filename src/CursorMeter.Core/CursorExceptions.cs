namespace CursorMeter;

public sealed class CursorAuthException : Exception
{
    public CursorAuthException(string message) : base(message) { }
}

public sealed class CursorUsageException : Exception
{
    public CursorUsageException(string message) : base(message) { }
}
