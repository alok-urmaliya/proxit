namespace T2Proxy.Exceptions;

public class BodyNotFoundException : ServerException
{
    internal BodyNotFoundException(string message) : base(message)
    {
    }
}