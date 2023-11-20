using Microsoft.Extensions.Configuration;
using T2Proxy.Helpers;
using T2Proxy_Console.Helpers;

namespace T2Proxy_Console
{
    public class Program
    {
        private static ProxyController controller;
        public static void Main(string[] args)
        {
            string root = System.AppContext.BaseDirectory;
            IConfiguration config = new ConfigurationBuilder()
                .SetBasePath(root)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            controller = new ProxyController(config);
            //Previous code above is the current implementation
            if (RunTime.IsWindows)
                ConsoleHelper.DisableQuickEditMode();

            controller.StartProxy();
            Console.WriteLine("Hit any key to exit..");
            Console.WriteLine();
            Console.Read();
            controller.Stop();
        }
    }
}