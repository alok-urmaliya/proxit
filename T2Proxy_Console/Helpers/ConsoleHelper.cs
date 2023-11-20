
using System.Runtime.InteropServices;

namespace T2Proxy_Console.Helpers
{
    internal class ConsoleHelper
    {
        private const uint EnableQuickEdit = 0x0040;
        private const int StdInputHandle = -10;
        [DllImport("kernel32.dll", SetLastError = true)]  
         
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll")]

        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll")]

        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint lpMode);
        // To Resolve Lag in the console.       
        internal static bool DisableQuickEditMode()
        {
            var consoleHandle = GetStdHandle(StdInputHandle);
            if (!GetConsoleMode(consoleHandle, out var consoleMode)) 
                return false;
            consoleMode &= ~EnableQuickEdit;
            
            if (!SetConsoleMode(consoleHandle, consoleMode))
                return false;
            return true; 
        }
    }  
}
