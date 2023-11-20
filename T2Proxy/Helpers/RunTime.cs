using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace T2Proxy.Helpers;

public static class RunTime
{
    private static readonly Lazy<bool> isRunningOnMono = new(() => Type.GetType("Mono.Runtime") != null);

#if NET461
 
    private static bool IsRunningOnWindows => true;

    private static bool IsRunningOnLinux => false;

    private static bool IsRunningOnMac => false;
#else
      
        private static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static bool IsRunningOnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        private static bool IsRunningOnMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif

    internal static bool IsRunningOnMono => isRunningOnMono.Value;

    public static bool IsLinux => IsRunningOnLinux;

    public static bool IsWindows => IsRunningOnWindows;

    public static bool IsUwpOnWindows => IsWindows && UwpHelper.IsRunningAsUwp();

    public static bool IsMac => IsRunningOnMac;

    private static bool? _isSocketReuseAvailable;

    public static bool IsSocketReuseAvailable()
    {
        if (_isSocketReuseAvailable != null)
            return _isSocketReuseAvailable.Value;

        try
        {
            if (IsWindows)
            {
                _isSocketReuseAvailable = true;
                return true;
            }

            var ver = Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

            if (ver == null)
                return false; 
            ver = ver.ToLower();
            if (ver.Contains(".netcoreapp"))
            {
                var versionString = ver.Replace(".netcoreapp,version=v", "");
                var versionArr = versionString.Split('.');
                var majorVersion = Convert.ToInt32(versionArr[0]);

                var result = majorVersion >= 3; 
                _isSocketReuseAvailable = result;
                return result;
            }

            _isSocketReuseAvailable = false;
            return false;
        }
        catch
        {
            _isSocketReuseAvailable = false;
            return false;
        }
    }

    private class UwpHelper
    {
        private const long AppmodelErrorNoPackage = 15700L;

        private static bool IsWindows7OrLower
        {
            get
            {
                var versionMajor = Environment.OSVersion.Version.Major;
                var versionMinor = Environment.OSVersion.Version.Minor;
                var version = versionMajor + (double)versionMinor / 10;
                return version <= 6.1;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength,
            StringBuilder packageFullName);

        internal static bool IsRunningAsUwp()
        {
            if (IsWindows7OrLower)
            {
                return false;
            }

            var length = 0;
            var sb = new StringBuilder(0);
            var result = GetCurrentPackageFullName(ref length, sb);

            sb = new StringBuilder(length);
            result = GetCurrentPackageFullName(ref length, sb);

            return result != AppmodelErrorNoPackage;
        }
    }
}