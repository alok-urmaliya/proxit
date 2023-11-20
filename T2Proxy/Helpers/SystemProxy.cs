using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Win32;
using T2Proxy.Models;

namespace T2Proxy.Helpers;

internal class HttpSystemProxyValue
{
    public HttpSystemProxyValue(string hostName, int port, ServerProtocolType protocolType)
    {
        HostName = hostName;
        Port = port;
        ProtocolType = protocolType;
    }

    internal string HostName { get; }

    internal int Port { get; }

    internal ServerProtocolType ProtocolType { get; }

    public override string ToString()
    {
        string protocol;
        switch (ProtocolType)
        {
            case ServerProtocolType.Http:
                protocol = ProxyServer.UriSchemeHttp;
                break;
            case ServerProtocolType.Https:
                protocol = ProxyServer.UriSchemeHttps;
                break;
            default:
                throw new Exception("Unsupported protocol type");
        }

        return $"{protocol}={HostName}:{Port}";
    }
}

[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleType",
    Justification = "Reviewed.")]
internal class SystemProxyManager
{
    private const string RegKeyInternetSettings = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";
    private const string RegAutoConfigUrl = "AutoConfigURL";
    private const string RegProxyEnable = "ProxyEnable";
    private const string RegProxyServer = "ProxyServer";
    private const string RegProxyOverride = "ProxyOverride";

    internal const int InternetOptionSettingsChanged = 39;
    internal const int InternetOptionRefresh = 37;

    private ServerInfo? originalValues;

    public SystemProxyManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (o, args) => RestoreOriginalSettings();
        if (Environment.UserInteractive && NativeMethods.GetConsoleWindow() != IntPtr.Zero)
        {
            var handler = new NativeMethods.ConsoleEventDelegate(eventType =>
            {
                if (eventType != 2) return false;

                RestoreOriginalSettings();
                return false;
            });
            NativeMethods.Handler = handler;

            NativeMethods.SetConsoleCtrlHandler(handler, true);
        }
    }

    internal void SetProxy(string hostname, int port, ServerProtocolType protocolType)
    {
        using (var reg = OpenInternetSettingsKey())
        {
            if (reg == null) return;

            SaveOriginalProxyConfiguration(reg);
            PrepareRegistry(reg);

            var existingContent = reg.GetValue(RegProxyServer) as string;
            var existingSystemProxyValues = ServerInfo.GetSystemProxyValues(existingContent);
            existingSystemProxyValues.RemoveAll(x => (protocolType & x.ProtocolType) != 0);
            if ((protocolType & ServerProtocolType.Http) != 0)
                existingSystemProxyValues.Add(new HttpSystemProxyValue(hostname, port, ServerProtocolType.Http));

            if ((protocolType & ServerProtocolType.Https) != 0)
                existingSystemProxyValues.Add(new HttpSystemProxyValue(hostname, port, ServerProtocolType.Https));

            reg.DeleteValue(RegAutoConfigUrl, false);
            reg.SetValue(RegProxyEnable, 1);
            reg.SetValue(RegProxyServer,
                string.Join(";", existingSystemProxyValues.Select(x => x.ToString()).ToArray()));

            Refresh();
        }
    }

    internal void RemoveProxy(ServerProtocolType protocolType, bool saveOriginalConfig = true)
    {
        using (var reg = OpenInternetSettingsKey())
        {
            if (reg == null) return;

            if (saveOriginalConfig) SaveOriginalProxyConfiguration(reg);

            if (reg.GetValue(RegProxyServer) != null)
            {
                var existingContent = reg.GetValue(RegProxyServer) as string;

                var existingSystemProxyValues = ServerInfo.GetSystemProxyValues(existingContent);
                existingSystemProxyValues.RemoveAll(x => (protocolType & x.ProtocolType) != 0);

                if (existingSystemProxyValues.Count != 0)
                {
                    reg.SetValue(RegProxyEnable, 1);
                    reg.SetValue(RegProxyServer,
                        string.Join(";", existingSystemProxyValues.Select(x => x.ToString()).ToArray()));
                }
                else
                {
                    reg.SetValue(RegProxyEnable, 0);
                    reg.SetValue(RegProxyServer, string.Empty);
                }
            }

            Refresh();
        }
    }

    internal void DisableAllProxy()
    {
        using (var reg = OpenInternetSettingsKey())
        {
            if (reg == null) return;

            SaveOriginalProxyConfiguration(reg);

            reg.SetValue(RegProxyEnable, 0);
            reg.SetValue(RegProxyServer, string.Empty);

            Refresh();
        }
    }

    internal void SetAutoProxyUrl(string url)
    {
        using (var reg = OpenInternetSettingsKey())
        {
            if (reg == null) return;

            SaveOriginalProxyConfiguration(reg);
            reg.SetValue(RegAutoConfigUrl, url);
            Refresh();
        }
    }

    internal void SetProxyOverride(string proxyOverride)
    {
        using (var reg = OpenInternetSettingsKey())
        {
            if (reg == null) return;

            SaveOriginalProxyConfiguration(reg);
            reg.SetValue(RegProxyOverride, proxyOverride);
            Refresh();
        }
    }

    internal void RestoreOriginalSettings()
    {
        if (originalValues == null) return;

        using (var reg = Registry.CurrentUser.OpenSubKey(RegKeyInternetSettings, true))
        {
            if (reg == null) return;

            var ov = originalValues;
            if (ov.AutoConfigUrl != null)
                reg.SetValue(RegAutoConfigUrl, ov.AutoConfigUrl);
            else
                reg.DeleteValue(RegAutoConfigUrl, false);

            if (ov.ProxyEnable.HasValue)
                reg.SetValue(RegProxyEnable, ov.ProxyEnable.Value);
            else
                reg.DeleteValue(RegProxyEnable, false);

            if (ov.ProxyServer != null)
                reg.SetValue(RegProxyServer, ov.ProxyServer);
            else
                reg.DeleteValue(RegProxyServer, false);

            if (ov.ProxyOverride != null)
                reg.SetValue(RegProxyOverride, ov.ProxyOverride);
            else
                reg.DeleteValue(RegProxyOverride, false);

            reg.Flush();

            originalValues = null;

            const int smShuttingdown = 0x2000;
            var windows7Version = new Version(6, 1);
            if (Environment.OSVersion.Version > windows7Version ||
                NativeMethods.GetSystemMetrics(smShuttingdown) == 0)
              
                Refresh();
        }
    }

    internal ServerInfo? GetProxyInfoFromRegistry()
    {
        using (var reg = OpenInternetSettingsKey())
        {
            if (reg == null) return null;

            return GetProxyInfoFromRegistry(reg);
        }
    }

    private ServerInfo GetProxyInfoFromRegistry(RegistryKey reg)
    {
        var pi = new ServerInfo(null,
            reg.GetValue(RegAutoConfigUrl) as string,
            reg.GetValue(RegProxyEnable) as int?,
            reg.GetValue(RegProxyServer) as string,
            reg.GetValue(RegProxyOverride) as string);

        return pi;
    }

    private void SaveOriginalProxyConfiguration(RegistryKey reg)
    {
        if (originalValues != null) return;

        originalValues = GetProxyInfoFromRegistry(reg);
    }
    private static void PrepareRegistry(RegistryKey reg)
    {
        if (reg.GetValue(RegProxyEnable) == null) reg.SetValue(RegProxyEnable, 0);

        if (reg.GetValue(RegProxyServer) == null || reg.GetValue(RegProxyEnable) as int? == 0)
            reg.SetValue(RegProxyServer, string.Empty);
    }

    private static void Refresh()
    {
        NativeMethods.InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        NativeMethods.InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    private static RegistryKey? OpenInternetSettingsKey()
    {
        return Registry.CurrentUser?.OpenSubKey(RegKeyInternetSettings, true);
    }
}