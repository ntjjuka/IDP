using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDTray.Infra
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IDTray");
        private static readonly string FilePath = Path.Combine(Dir, "agent.log");

        public static void Info(string msg) => Write("INFO", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Dir);
                    File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {msg}\r\n");
                }
            }
            catch { /* ignore */ }
        }
    }
}
