using System;
using System.IO;

namespace Hum.App.Services;

/// <summary>Tiny diagnostics log in %LOCALAPPDATA%\Hum\log.txt (truncated at 1 MB).</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static readonly string PathName = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hum", "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 1_000_000) File.Delete(PathName);
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never break the app
        }
    }
}
