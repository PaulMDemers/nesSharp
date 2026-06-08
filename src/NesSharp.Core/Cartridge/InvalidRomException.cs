namespace NesSharp.Core.Cartridge;

public sealed class InvalidRomException : Exception
{
    public InvalidRomException(string message)
        : base(message)
    {
    }
}

