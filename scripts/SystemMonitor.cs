using Godot;
using System;
using System.Diagnostics;

using System.Runtime.Versioning;

public partial class SystemMonitor : Node
{
    private PerformanceCounter _cpuCounter;
    private PerformanceCounter _ramCounter;
    private Timer _timer;
    private float _pRam = 0;
    private float _pCpu = 0;
    private float _totalRamMb = 0;
    private float _pServerRam = 0;

    [Signal]
    public delegate void StatsUpdatedEventHandler(float cpuPercent, float ramPercent, float serverRamPercent);

    [SupportedOSPlatform("windows")]
    public override void _Ready()
    {
        if (OS.GetName() == "Windows")
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            // Initial call to initialize counter
            _cpuCounter.NextValue();

            // Get total physical RAM for accurate % calculation
            _totalRamMb = GetTotalPhysicalMemory();

            _timer = new Timer();
            _timer.WaitTime = 5.0f; // Update every 5 seconds
            _timer.Connect("timeout", new Callable(this, MethodName.OnTimerTimeout));
            AddChild(_timer);
            _timer.Start();

            // Perform an initial check immediately after a short delay to populate UI
            GetTree().CreateTimer(0.5f).Connect("timeout", new Callable(this, MethodName.OnTimerTimeout));
        }
    }

    [SupportedOSPlatform("windows")]
    private float GetTotalPhysicalMemory()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("wmic", "ComputerSystem get TotalPhysicalMemory")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            string[] lines = output.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1 && long.TryParse(lines[1].Trim(), out long bytes))
            {
                return bytes / 1024f / 1024f;
            }
        }
        catch { }
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private void OnTimerTimeout()
    {
        if (_cpuCounter != null && _ramCounter != null)
        {
            _pCpu = _cpuCounter.NextValue();
            float availableRamMb = _ramCounter.NextValue();

            if (_totalRamMb > 0)
            {
                // Usage % = (Total - Available) / Total
                _pRam = 100f * (1.0f - (availableRamMb / _totalRamMb));
            }
            else
            {
                // Fallback to committed bytes if wmic failed
                using (var committedCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use"))
                {
                    _pRam = committedCounter.NextValue();
                }
            }

            // Poll active server process RAM if running
            ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
            if (sm.IsRunning && sm.ActiveProcess != null && !sm.ActiveProcess.HasExited)
            {
                try
                {
                    sm.ActiveProcess.Refresh();
                    float usedMb = sm.ActiveProcess.PrivateMemorySize64 / 1024f / 1024f; // Use Private Memory for better accuracy
                    float maxMb = sm.MaxRamMb;
                    _pServerRam = (usedMb / maxMb) * 100f;

                    // Cap at 100% just in case of slight overflow or JVM overhead
                    if (_pServerRam > 100f) _pServerRam = 100f;
                }
                catch { _pServerRam = 0; }
            }
            else
            {
                _pServerRam = 0;
            }

            EmitSignal(SignalName.StatsUpdated, _pCpu, _pRam, _pServerRam);
        }
    }
}
