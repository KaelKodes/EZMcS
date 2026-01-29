using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

public static class AffinityHelper
{
    [SupportedOSPlatform("windows")]
    public static long GetSmartMask()
    {
        int logicalCores = Environment.ProcessorCount;

        // Default to all cores if something goes wrong
        long mask = 0;
        for (int i = 0; i < logicalCores; i++) mask |= (1L << i);

        try
        {
            // Simple logic for common high-end gaming CPUs (Intel Hybrid or AMD CCD)
            // Goal: Pick 4 to 8 physical cores, avoiding E-cores and crossing CCDs.

            if (IsIntelHybrid())
            {
                // Intel 12th/13th/14th Gen: P-cores are first.
                // i9-14900K: 8 P-cores (16 threads) + 16 E-cores.
                // We want only physical P-cores (even indices).
                mask = 0;
                int pCoreThreads = GetPCoreThreadCount();
                for (int i = 0; i < pCoreThreads; i += 2)
                {
                    mask |= (1L << i);
                }
            }
            else if (IsAMD())
            {
                // AMD CCD Logic: Keep it on the first CCD.
                // Most Ryzen CCDs are 6 or 8 cores.
                mask = 0;
                int coresPerCCD = GetAMDCoresPerCCD();
                for (int i = 0; i < coresPerCCD * 2; i += 2)
                {
                    mask |= (1L << i);
                }
            }
            else
            {
                // Generic: Pick even-numbered cores up to 8
                mask = 0;
                for (int i = 0; i < Math.Min(logicalCores, 16); i += 2)
                {
                    mask |= (1L << i);
                }
            }
        }
        catch { }

        return mask;
    }

    private static bool IsIntelHybrid()
    {
        // Simple heuristic: "Intel" in name and high core count (>16 logical)
        string name = GetCpuName().ToLower();
        return name.Contains("intel") && Environment.ProcessorCount > 16;
    }

    private static int GetPCoreThreadCount()
    {
        // For i9-13900/14900: 8 P-cores = 16 threads.
        // For i7-13700/14700: 8 P-cores = 16 threads.
        // For i5-13600/14600: 6 P-cores = 12 threads.
        // Hardcoding common high-end values for now or assuming 16 threads is safe for i7/i9.
        string name = GetCpuName();
        if (name.Contains("i9-") || name.Contains("i7-")) return 16;
        if (name.Contains("i5-")) return 12;
        return 8; // Fallback
    }

    private static bool IsAMD()
    {
        return GetCpuName().ToLower().Contains("amd") || GetCpuName().ToLower().Contains("ryzen");
    }

    private static int GetAMDCoresPerCCD()
    {
        // Most Ryzen 9 (5900/7900) have 6 cores per CCD.
        // Most Ryzen 9 (5950/7950) have 8 cores per CCD.
        // Ryzen 5/7 usually have 6 or 8 and only one CCD.
        int totalCores = Environment.ProcessorCount / 2;
        if (totalCores > 8) return 6; // Likely a 12-core parts split 6+6
        return totalCores; // Single CCD
    }

    private static string GetCpuName()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            {
                if (key != null) return key.GetValue("ProcessorNameString")?.ToString() ?? "";
            }
        }
        catch { }
        return "Unknown";
    }
}
