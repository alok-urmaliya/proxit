// http://pinvoke.net/default.aspx/secur32/InitializeSecurityContext.html

using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using T2Proxy.Http;

namespace T2Proxy.Network.WinAuth.Security;

using static Common;

internal class WinAuthEndPoint
{
    private const string AuthStateKey = "AuthState";

    internal static byte[]? AcquireInitialSecurityToken(string hostname, string authScheme, InternalDataStore data)
    {
        byte[]? token;

        var serverToken = new SecurityBufferDesciption();

        var clientToken = new SecurityBufferDesciption(MaximumTokenSize);

        try
        {
            var state = new State();

            var result = AcquireCredentialsHandle(
                WindowsIdentity.GetCurrent().Name,
                authScheme,
                SecurityCredentialsOutbound,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                ref state.Credentials,
                ref NewLifeTime);

            if (result != SuccessfulResult) return null;

            result = InitializeSecurityContext(ref state.Credentials,
                IntPtr.Zero,
                hostname,
                StandardContextAttributes,
                0,
                SecurityNativeDataRepresentation,
                ref serverToken,
                0,
                out state.Context,
                out clientToken,
                out NewContextAttributes,
                out NewLifeTime);

            if (result != IntermediateResult) return null;

            state.AuthState = State.WinAuthState.InitialToken;
            token = clientToken.GetBytes();
            data.Add(AuthStateKey, state);
        }
        finally
        {
            DisposeToken(clientToken);
            DisposeToken(serverToken);
        }

        return token;
    }

    internal static byte[]? AcquireFinalSecurityToken(string hostname, byte[] serverChallenge, InternalDataStore data)
    {
        byte[]? token;

        var serverToken = new SecurityBufferDesciption(serverChallenge);

        var clientToken = new SecurityBufferDesciption(MaximumTokenSize);

        try
        {
            var state = data.GetAs<State>(AuthStateKey);

            state.UpdatePresence();

            var result = InitializeSecurityContext(ref state.Credentials,
                ref state.Context,
                hostname,
                StandardContextAttributes,
                0,
                SecurityNativeDataRepresentation,
                ref serverToken,
                0,
                out state.Context,
                out clientToken,
                out NewContextAttributes,
                out NewLifeTime);

            if (result != SuccessfulResult) return null;

            state.AuthState = State.WinAuthState.FinalToken;
            token = clientToken.GetBytes();
        }
        finally
        {
            DisposeToken(clientToken);
            DisposeToken(serverToken);
        }

        return token;
    }

    private static void DisposeToken(SecurityBufferDesciption clientToken)
    {
        if (clientToken.pBuffers != IntPtr.Zero)
        {
            if (clientToken.cBuffers == 1)
            {
                var thisSecBuffer =
                    (SecurityBuffer)Marshal.PtrToStructure(clientToken.pBuffers, typeof(SecurityBuffer));
                DisposeSecBuffer(thisSecBuffer);
            }
            else
            {
                for (var index = 0; index < clientToken.cBuffers; index++)
                {
                    var currentOffset = index * Marshal.SizeOf(typeof(Buffer));
                    var secBufferpvBuffer = Marshal.ReadIntPtr(clientToken.pBuffers,
                        currentOffset + Marshal.SizeOf(typeof(int)) + Marshal.SizeOf(typeof(int)));
                    Marshal.FreeHGlobal(secBufferpvBuffer);
                }
            }

            Marshal.FreeHGlobal(clientToken.pBuffers);
            clientToken.pBuffers = IntPtr.Zero;
        }
    }

    private static void DisposeSecBuffer(SecurityBuffer thisSecBuffer)
    {
        if (thisSecBuffer.pvBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(thisSecBuffer.pvBuffer);
            thisSecBuffer.pvBuffer = IntPtr.Zero;
        }
    }
    internal static bool ValidateWinAuthState(InternalDataStore data, State.WinAuthState expectedAuthState)
    {
        var stateExists = data.TryGetValueAs(AuthStateKey, out State? state);

        if (expectedAuthState == State.WinAuthState.Unauthorized)
            return !stateExists ||
                   state!.AuthState == State.WinAuthState.Unauthorized ||
                   state.AuthState ==
                   State.WinAuthState.Authorized;

        if (expectedAuthState == State.WinAuthState.InitialToken)
            return stateExists &&
                   (state!.AuthState == State.WinAuthState.InitialToken ||
                    state.AuthState ==
                    State.WinAuthState.Authorized);

        throw new Exception("Unsupported validation of WinAuthState");
    }

    internal static void AuthenticatedResponse(InternalDataStore data)
    {
        if (data.TryGetValueAs(AuthStateKey, out State? state))
        {
            state!.AuthState = State.WinAuthState.Authorized;
            state.UpdatePresence();
        }
    }

    #region Native calls to secur32.dll

    [DllImport("secur32.dll", SetLastError = true)]
    private static extern int InitializeSecurityContext(ref SecurityHandle phCredential,
        IntPtr phContext,
        string pszTargetName,
        int fContextReq,
        int reserved1,
        int targetDataRep,
        ref SecurityBufferDesciption pInput,
        int reserved2,
        out SecurityHandle phNewContext,
        out SecurityBufferDesciption pOutput,
        out uint pfContextAttr,
        out SecurityInteger ptsExpiry);

    [DllImport("secur32", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int InitializeSecurityContext(ref SecurityHandle phCredential,
        ref SecurityHandle phContext,
        string pszTargetName,
        int fContextReq,
        int reserved1,
        int targetDataRep,
        ref SecurityBufferDesciption secBufferDesc, 
        int reserved2,
        out SecurityHandle phNewContext,
        out SecurityBufferDesciption pOutput, 
        out uint pfContextAttr,
        out SecurityInteger ptsExpiry); 

    [DllImport("secur32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    private static extern int AcquireCredentialsHandle(
        string pszPrincipal, 
        string pszPackage, 
        int fCredentialUse,
        IntPtr pAuthenticationId, 
        IntPtr pAuthData, 
        int pGetKeyFn, 
        IntPtr pvGetKeyArgument, 
        ref SecurityHandle phCredential, 
        ref SecurityInteger ptsExpiry); 

    #endregion
}