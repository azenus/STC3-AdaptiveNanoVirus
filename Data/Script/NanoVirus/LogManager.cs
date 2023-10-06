using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kage.HackingComputer
{
    class LogManager
    {
        private static Queue<string> buffer = new Queue<string>();
        private static TextWriter stream = null;

        public static bool HasBeenInitialize
        {
            get
            {
                return stream != null;
            }
        }

        public static void Initialize()
        {
            if (MyAPIGateway.Utilities != null)
            {
                stream = MyAPIGateway.Utilities.WriteFileInLocalStorage("Log.txt", typeof(LogManager));
                while (buffer.Count > 0)
                {
                    string line = buffer.Dequeue();
                    WriteRaw(line);
                }
                stream.Flush();
            }
        }

        public static void Unload()
        {
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }
        }

        public static void WriteException(Exception e)
        {
            WriteRaw("Exception: " + e.Message);
            WriteRaw("Source: " + e.Source);
            WriteRaw("Stack Trace: " + e.StackTrace);
            if (HasBeenInitialize)
                stream.Flush();
        }

        public static void WriteLine(string line)
        {
            WriteRaw(line);
            if (HasBeenInitialize)
                stream.Flush();
        }

        private static void WriteRaw(string line)
        {
            if (!HasBeenInitialize)
                Initialize();

            if (HasBeenInitialize)
            {
                stream.WriteLine(line);
            }
            else
            {
                buffer.Enqueue(line);
            }
        }


    }
}
