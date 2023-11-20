namespace T2Proxy.Models;

public enum ServerAuthenticationResult
{
    Success,
    Failure,
    ContinuationNeeded
}
public class ServerAuthenticationContext
{
    public ServerAuthenticationResult Result { get; set; }

    public string? Continuation { get; set; }

    public static ServerAuthenticationContext Failed()
    {
        return new ServerAuthenticationContext
        {
            Result = ServerAuthenticationResult.Failure,
            Continuation = null
        };
    }

    public static ServerAuthenticationContext Succeeded()
    {
        return new ServerAuthenticationContext
        {
            Result = ServerAuthenticationResult.Success,
            Continuation = null
        };
    }
}