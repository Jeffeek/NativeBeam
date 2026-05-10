namespace NativeBeam.Pdf.Cdp;

public sealed class CdpException : Exception
{
    public int Code { get; }

    public CdpException()
    {
    }

    public CdpException(string message) : base(message)
    {
    }

    public CdpException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public CdpException(int code, string message) : base(message)
    {
        Code = code;
    }
}
