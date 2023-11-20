using T2Proxy.EventArguments;

namespace T2Proxy_Console
{
    public static class ProxyEventArgsBaseExtensions
    {
        public static ClientState GetState(this ServerEventArgsBase args)
        {
            if (args.ClientUserData == null) 
                args.ClientUserData = new ClientState();
            return (ClientState)args.ClientUserData;
        }
    }
}
