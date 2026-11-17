using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteambirdsLauncher.Core;

// Win32 calls used by the launcher.
internal static class NativeMethods
{
    // ---------------------------------------------------------------- job object
    // The server runs inside a job that is closed together with the launcher: if the launcher crashes or is killed,
    // Windows ends the server too, so the ports are not left taken by an orphan server.

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationData
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformationData info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private static readonly object JobGate = new object();
    private static IntPtr killOnCloseJob = IntPtr.Zero;

    // Puts the process in the launcher's job. Failing is not fatal: the launcher still stops the server on exit.
    public static bool AssignToKillOnCloseJob(Process process)
    {
        try
        {
            lock (JobGate)
            {
                if (killOnCloseJob == IntPtr.Zero)
                {
                    IntPtr job = CreateJobObject(IntPtr.Zero, null);
                    if (job == IntPtr.Zero) return false;
                    var info = new JobObjectExtendedLimitInformationData();
                    info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                    uint size = (uint)Marshal.SizeOf<JobObjectExtendedLimitInformationData>();
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, size)) return false;
                    // The handle stays open for the whole life of the launcher; Windows closes it when the launcher ends.
                    killOnCloseJob = job;
                }
                return AssignProcessToJobObject(killOnCloseJob, process.Handle);
            }
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- rich text box helpers

    public const int WmSetRedraw = 0x000B;
    public const int EmGetScrollPos = 0x0400 + 221;
    public const int EmSetScrollPos = 0x0400 + 222;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref Point32 lParam);
}
