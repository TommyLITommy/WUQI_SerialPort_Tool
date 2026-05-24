using System;
using System.Runtime.InteropServices;

public static class NativeConsole
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static void Open()
    {
        AllocConsole();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
    }

    public static void Close()
    {
        FreeConsole();
    }
}
