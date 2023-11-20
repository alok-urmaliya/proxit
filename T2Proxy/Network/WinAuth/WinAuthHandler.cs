using System;
using T2Proxy.Http;
using T2Proxy.Network.WinAuth.Security;

namespace T2Proxy.Network.WinAuth;

internal static class WinAuthHandler
{
    internal static string GetInitialAuthToken(string serverHostname, string authScheme, InternalDataStore data)
    {
        var tokenBytes = WinAuthEndPoint.AcquireInitialSecurityToken(serverHostname, authScheme, data);
        return string.Concat(" ", Convert.ToBase64String(tokenBytes));
    }

    internal static string GetFinalAuthToken(string serverHostname, string serverToken, InternalDataStore data)
    {
        var tokenBytes =
            WinAuthEndPoint.AcquireFinalSecurityToken(serverHostname, Convert.FromBase64String(serverToken),
                data);

        return string.Concat(" ", Convert.ToBase64String(tokenBytes));
    }
}