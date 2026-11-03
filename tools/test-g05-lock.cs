using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
class G05LockProbe
{
    static int Main(string[] args)
    {
        try
        {
            if (args[0] == "probe-busy")
            {
                try { using (new FileStream(args[1], FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { } return 1; }
                catch (IOException e) { int code = Marshal.GetHRForException(e) & 0xffff; return code == 32 || code == 33 ? 0 : 3; }
            }
            if (args[0] == "hold")
            {
                using (new FileStream(args[1], FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    File.WriteAllText(args[2], "ready");
                    for (int i = 0; i < 300; ++i)
                    {
                        if (File.Exists(args[3])) return 0;
                        Thread.Sleep(100);
                    }
                    return 4;
                }
            }
            return 5;
        }
        catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 2; }
    }
}
