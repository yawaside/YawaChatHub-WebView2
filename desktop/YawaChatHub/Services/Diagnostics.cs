using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace YawaChatHub.Services;

/// <summary>
/// Simple diagnostics helpers: logs count and memory of msedgewebview2 processes.
/// </summary>
public static class Diagnostics
{
    private static readonly object Sync = new();

    public static void LogWebViewProcesses(string context = "")
    {
        try
        {
            var procs = Process.GetProcessesByName("msedgewebview2");
            var totalBytes = procs.Sum(p => {
                try { return p.PrivateMemorySize64; } catch { return 0L; }
            });
            var msg = $"WebView2 processes: count={procs.Length}, totalMB={totalBytes / 1024 / 1024} {context}".Trim();
            WriteLog(msg);
        }
        catch (Exception ex)
        {
            WriteLog("LogWebViewProcesses failed: " + ex.Message);
        }
    }

    public static void WriteLog(string message)
    {
        try
        {
            lock (Sync)
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YawaChatHub", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "webview-diagnostics.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
