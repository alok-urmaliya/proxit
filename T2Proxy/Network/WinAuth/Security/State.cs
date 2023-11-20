using System;

namespace T2Proxy.Network.WinAuth.Security;

internal class State
{
    public enum WinAuthState
    {
        Unauthorized,
        InitialToken,
        FinalToken,
        Authorized
    }

    internal WinAuthState AuthState;
    internal Common.SecurityHandle Context;

    internal Common.SecurityHandle Credentials;

    internal DateTime LastSeen;

    internal State()
    {
        Credentials = new Common.SecurityHandle(0);
        Context = new Common.SecurityHandle(0);

        LastSeen = DateTime.UtcNow;
        AuthState = WinAuthState.Unauthorized;
    }

    internal void ResetHandles()
    {
        Credentials.Reset();
        Context.Reset();
        AuthState = WinAuthState.Unauthorized;
    }

    internal void UpdatePresence()
    {
        LastSeen = DateTime.UtcNow;
    }
}