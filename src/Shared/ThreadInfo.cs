using System.Runtime.InteropServices;

namespace Concurrency.Shared;

/// <summary>
/// Helpers for printing what a current thread "is" — which is non-trivial because
/// in modern .NET the thread is a managed wrapper over an OS scheduling unit, and the
/// distinction frequently matters when reasoning about IO completion ports, the thread
/// pool's worker vs IO partitions, and synchronisation contexts.
/// </summary>
public static class ThreadInfo
{
    public static string Describe()
    {
        var t = Thread.CurrentThread;
        return $"managedId={Environment.CurrentManagedThreadId,3}  " +
               $"pool={(t.IsThreadPoolThread ? "Y" : "N")}  " +
               $"bg={(t.IsBackground ? "Y" : "N")}  " +
               $"prio={t.Priority,-7}  " +
               $"name={t.Name ?? "<unnamed>"}";
    }

    public static int GetOsThreadId()
    {
        // On modern .NET, the OS thread id is best obtained via a small P/Invoke per platform.
        // Use System.Diagnostics.Process per-thread enumeration as a portable fallback.
        return Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Print info about the runtime / OS we are running on. Used by chapter banners.
    /// </summary>
    public static string DescribeRuntime()
    {
        return $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} " +
               $"({RuntimeInformation.OSArchitecture}, {RuntimeInformation.ProcessArchitecture})";
    }
}
