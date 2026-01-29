using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

public static class AffinityHelper
{
    public class CoreInfo
    {
        public int Index { get; set; }
        public string Type { get; set; } // P-Core, E-Core, CCD0, CCD1, etc.
        public bool IsLogical { get; set; } // Is it the second thread of an SMT/HT pair?
        public string GroupName { get; set; }
    }

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

    public static List<CoreInfo> GetCoreTopology()
    {
        var topology = new List<CoreInfo>();
        int logicalCores = Environment.ProcessorCount;
        string cpuName = GetCpuName().ToLower();

        bool isIntel = cpuName.Contains("intel");
        bool isAMD = cpuName.Contains("amd") || cpuName.Contains("ryzen");

        if (isIntel && logicalCores > 16)
        {
            // Intel Hybrid (12th-14th Gen)
            int pCoreThreads = GetPCoreThreadCount();
            for (int i = 0; i < logicalCores; i++)
            {
                bool isP = i < pCoreThreads;
                topology.Add(new CoreInfo
                {
                    Index = i,
                    Type = isP ? "P-Core" : "E-Core",
                    GroupName = isP ? "Performance Cores" : "Efficiency Cores",
                    IsLogical = isP && (i % 2 != 0)
                });
            }
        }
        else if (isAMD)
        {
            // AMD CCD Logic
            int coresPerCCD = GetAMDCoresPerCCD();
            int threadsPerCCD = coresPerCCD * 2;
            for (int i = 0; i < logicalCores; i++)
            {
                int ccdIndex = i / threadsPerCCD;
                topology.Add(new CoreInfo
                {
                    Index = i,
                    Type = $"CCD {ccdIndex}",
                    GroupName = $"Chiplet {ccdIndex}",
                    IsLogical = (i % 2 != 0)
                });
            }
        }
        else
        {
            // Generic fallback (Assuming SMT pairs 0/1, 2/3...)
            for (int i = 0; i < logicalCores; i++)
            {
                topology.Add(new CoreInfo
                {
                    Index = i,
                    Type = "Core",
                    GroupName = "Processor",
                    IsLogical = (i % 2 != 0)
                });
            }
        }

        return topology;
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
