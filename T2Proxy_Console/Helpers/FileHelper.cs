using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace T2Proxy_Console.Helpers
{
    public class FileHelper
    {
        private static string directoryName = Path.Combine(AppContext.BaseDirectory + "SessionFiles");
        private static string fileName = "\\Session_" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")+".json";

        public FileHelper() { }

        public static void WriteDataToFile(List<string> SessionData)
        {
            string filePath = Path.Combine(directoryName + fileName);
            var jsonData = JsonConvert.SerializeObject(SessionData, Formatting.Indented);
            File.WriteAllText(filePath, jsonData);
        }
    }
}
