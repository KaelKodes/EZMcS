using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Linq;
using System.Text.RegularExpressions;

public partial class ServerManager : Node
{
    [Signal] public delegate void LogReceivedEventHandler(string profileName, string message, bool isError);
    [Signal] public delegate void StatusChangedEventHandler(string profileName, string newStatus);
    [Signal] public delegate void PlayerJoinedEventHandler(string profileName, string playerName);
    [Signal] public delegate void PlayerLeftEventHandler(string profileName, string playerName);
    [Signal] public delegate void ModConflictDetectedEventHandler(string profileName, string[] modNames, string[] filenames);

    private Dictionary<string, Process> _serverProcesses = new Dictionary<string, Process>();
    private Dictionary<string, List<string>> _onlinePlayers = new Dictionary<string, List<string>>();
    private Dictionary<string, string> _serverPaths = new Dictionary<string, string>();
    private Dictionary<string, int> _serverMaxRam = new Dictionary<string, int>();

    public bool IsAnyRunning => _serverProcesses.Values.Any(p => p != null && !p.HasExited);
    public bool IsRunning(string profileName) => _serverProcesses.ContainsKey(profileName) && _serverProcesses[profileName] != null && !_serverProcesses[profileName].HasExited;

    public ReadOnlyCollection<string> GetOnlinePlayers(string profileName)
    {
        if (_onlinePlayers.ContainsKey(profileName)) return _onlinePlayers[profileName].AsReadOnly();
        return new List<string>().AsReadOnly();
    }

    public Process GetActiveProcess(string profileName)
    {
        if (_serverProcesses.ContainsKey(profileName)) return _serverProcesses[profileName];
        return null;
    }

    public int GetMaxRamMb(string profileName)
    {
        if (_serverMaxRam.ContainsKey(profileName)) return _serverMaxRam[profileName];
        return 2048; // Default fallback
    }

    public void StartServer(string profileName, string path, string jar, string maxRam = "4G", string minRam = "2G", string javaPath = "", string extraFlags = "", string modsPath = "")
    {
        if (IsRunning(profileName)) return;

        _serverPaths[profileName] = path;
        _serverMaxRam[profileName] = ParseRamToMb(maxRam);

        // Reset conflict detection for new server run
        _conflictDialogShown[profileName] = false;
        if (_pendingConflicts.ContainsKey(profileName))
            _pendingConflicts[profileName].Clear();

        // Kill any zombie processes locking this folder first
        KillZombieProcesses(profileName, path);

        // Perform Smart Mod Sync
        string serverModsPath = Path.Combine(path, "mods");
        ModSyncHelper.SyncMods(modsPath, serverModsPath, (msg) => EmitSignal(SignalName.LogReceived, profileName, msg, false));

        // Fix Point Blank and other mods expecting specific folders
        try
        {
            string pbPath = Path.Combine(path, "pointblank");
            if (!Directory.Exists(pbPath))
            {
                Directory.CreateDirectory(pbPath);
                EmitSignal(SignalName.LogReceived, profileName, "[System] Created missing 'pointblank' directory for compatibility.", false);
            }
        }
        catch { }

        string actualJavaPath = javaPath;
        // Intelligent Java Detection
        if (string.IsNullOrWhiteSpace(javaPath) || javaPath.ToLower() == "auto" || javaPath == "Default (java)")
        {
            int requiredVersion = 0;
            if (jar == "FORGE_INSTALLED" || jar == "NEOFORGE_INSTALLED")
            {
                // For modern Forge markers, we assume at least Java 17
                requiredVersion = 17;

                // Try to be more precise by looking at the directory structure
                try
                {
                    string libDir = Path.Combine(path, "libraries", "net", "minecraftforge", "forge");
                    if (!Directory.Exists(libDir)) libDir = Path.Combine(path, "libraries", "net", "neoforged", "neoforge");

                    if (Directory.Exists(libDir))
                    {
                        var firstVerDir = Directory.GetDirectories(libDir).FirstOrDefault();
                        if (firstVerDir != null)
                        {
                            var verMatch = Regex.Match(Path.GetFileName(firstVerDir), @"([0-9]+\.[0-9]+)");
                            if (verMatch.Success) requiredVersion = JavaHelper.GetJavaForMCVersion(verMatch.Groups[1].Value);
                        }
                    }
                }
                catch { }
            }
            else
            {
                string fullJarPath = Path.Combine(path, jar);
                requiredVersion = JavaHelper.DetectRequiredJava(fullJarPath);
            }

            if (requiredVersion > 0)
            {
                EmitSignal(SignalName.LogReceived, profileName, $"[System] Detected Minecraft requirement: Java {requiredVersion}", false);
                actualJavaPath = JavaHelper.GetBestJavaPath(requiredVersion);
                EmitSignal(SignalName.LogReceived, profileName, $"[System] Using Java Path: {actualJavaPath}", false);
            }
            else
            {
                actualJavaPath = "java";
            }
        }

        string javaArgs = $"-Xmx{maxRam} -Xms{minRam} {extraFlags}";
        string finalArgs = $"{javaArgs} -jar \"{jar}\" nogui";

        // Modern Forge/NeoForge (1.17+) Launch Logic
        if (jar == "FORGE_INSTALLED" || jar == "NEOFORGE_INSTALLED")
        {
            EmitSignal(SignalName.LogReceived, profileName, $"[System] Detecting modern {jar.Replace("_INSTALLED", "")} launch arguments...", false);

            string userArgsPath = Path.Combine(path, "user_jvm_args.txt");
            string winArgsPath = "";

            // Search libraries for win_args.txt (Windows) or unix_args.txt (Other)
            string searchPattern = OperatingSystem.IsWindows() ? "win_args.txt" : "unix_args.txt";
            try
            {
                string librariesDir = Path.Combine(path, "libraries");
                if (Directory.Exists(librariesDir))
                {
                    // Prefer searching net/minecraftforge or net/neoforged first to avoid conflicts if multiple loaders present
                    string forgeLibPath = Path.Combine(librariesDir, "net", "minecraftforge", "forge");
                    if (jar == "NEOFORGE_INSTALLED") forgeLibPath = Path.Combine(librariesDir, "net", "neoforged", "neoforge");

                    if (Directory.Exists(forgeLibPath))
                    {
                        winArgsPath = Directory.GetFiles(forgeLibPath, searchPattern, SearchOption.AllDirectories).LastOrDefault();
                    }
                    else
                    {
                        winArgsPath = Directory.GetFiles(librariesDir, searchPattern, SearchOption.AllDirectories).FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                EmitSignal(SignalName.LogReceived, profileName, $"[Warning] Failed to search for Forge args: {ex.Message}", false);
            }

            if (!string.IsNullOrEmpty(winArgsPath))
            {
                string relativeWinArgs = Path.GetRelativePath(path, winArgsPath);
                string userArgsFlag = File.Exists(userArgsPath) ? $"@user_jvm_args.txt " : "";
                finalArgs = $"{javaArgs} {userArgsFlag}@{relativeWinArgs} nogui";
                EmitSignal(SignalName.LogReceived, profileName, $"[System] Found Forge args: {relativeWinArgs}", false);
            }
            else
            {
                EmitSignal(SignalName.LogReceived, profileName, "[Error] Could not find win_args.txt. Forge installation might be incomplete.", true);
                // Fall back to trying to find a jar anyway
                string fallbackJar = Directory.GetFiles(path, "forge-*.jar").FirstOrDefault(f => !f.Contains("installer"));
                if (fallbackJar != null)
                {
                    finalArgs = $"{javaArgs} -jar \"{Path.GetFileName(fallbackJar)}\" nogui";
                }
            }
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = Directory.Exists(actualJavaPath) ? JavaHelper.FindJavaExeInDir(actualJavaPath) ?? actualJavaPath : actualJavaPath,
            Arguments = finalArgs,
            WorkingDirectory = path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process = new Process { StartInfo = startInfo };
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (s, e) => { if (e.Data != null) CallDeferred(MethodName.HandleLog, profileName, e.Data, false); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) CallDeferred(MethodName.HandleLog, profileName, e.Data, true); };
        process.Exited += (s, e) => CallDeferred(MethodName.HandleExit, profileName);

        _serverProcesses[profileName] = process;
        _onlinePlayers[profileName] = new List<string>();

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            EmitSignal(SignalName.StatusChanged, profileName, "Starting");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to start Minecraft server ({profileName}): " + ex.Message);
            EmitSignal(SignalName.LogReceived, profileName, "Failed to start: " + ex.Message, true);
            EmitSignal(SignalName.StatusChanged, profileName, "Stopped");
            _serverProcesses.Remove(profileName);
        }
    }

    public void SendCommand(string profileName, string command)
    {
        if (IsRunning(profileName))
        {
            _serverProcesses[profileName].StandardInput.WriteLine(command);
            EmitSignal(SignalName.LogReceived, profileName, $"> {command}", false);
        }
    }

    public void StopServer(string profileName)
    {
        if (IsRunning(profileName))
        {
            SendCommand(profileName, "stop");
            EmitSignal(SignalName.StatusChanged, profileName, "Stopping");
        }
    }

    public void KillServer(string profileName)
    {
        if (IsRunning(profileName))
        {
            _serverProcesses[profileName].Kill();
            EmitSignal(SignalName.StatusChanged, profileName, "Killed");
        }
    }

    private void HandleLog(string profileName, string message, bool isError)
    {
        EmitSignal(SignalName.LogReceived, profileName, message, isError);
        ParseLog(profileName, message);
    }

    private void HandleExit(string profileName)
    {
        EmitSignal(SignalName.StatusChanged, profileName, "Stopped");
        if (_serverProcesses.ContainsKey(profileName))
        {
            _serverProcesses[profileName].Dispose();
            _serverProcesses.Remove(profileName);
        }
        if (_onlinePlayers.ContainsKey(profileName))
        {
            _onlinePlayers[profileName].Clear();
        }

        // EULA Check
        CheckEula(profileName);
    }

    private void CheckEula(string profileName)
    {
        if (!_serverPaths.ContainsKey(profileName)) return;

        string eulaPath = Path.Combine(_serverPaths[profileName], "eula.txt");
        if (File.Exists(eulaPath))
        {
            string content = File.ReadAllText(eulaPath);
            if (content.Contains("eula=false"))
            {
                EmitSignal(SignalName.LogReceived, profileName, "[System] Server stopped because EULA is not accepted. You can fix this in the 'Edit Server Config' menu or by editing eula.txt.", true);
            }
        }
    }

    public void AcceptEula(string profileName)
    {
        if (!_serverPaths.ContainsKey(profileName)) return;

        string eulaPath = Path.Combine(_serverPaths[profileName], "eula.txt");
        if (File.Exists(eulaPath))
        {
            string content = File.ReadAllText(eulaPath);
            content = content.Replace("eula=false", "eula=true");
            File.WriteAllText(eulaPath, content);
            EmitSignal(SignalName.LogReceived, profileName, "[System] EULA accepted.", false);
        }
        else
        {
            File.WriteAllLines(eulaPath, new string[] { "# Minecraft EULA", "eula=true" });
            EmitSignal(SignalName.LogReceived, profileName, "[System] eula.txt created and EULA accepted.", false);
        }
    }

    private void ParseLog(string profileName, string message)
    {
        if (message.Contains("joined the game"))
        {
            string playerName = ExtractPlayerName(message);
            if (!string.IsNullOrEmpty(playerName))
            {
                if (!_onlinePlayers[profileName].Contains(playerName)) _onlinePlayers[profileName].Add(playerName);
                EmitSignal(SignalName.PlayerJoined, profileName, playerName);
            }
        }
        else if (message.Contains("left the game"))
        {
            string playerName = ExtractPlayerName(message);
            if (!string.IsNullOrEmpty(playerName))
            {
                _onlinePlayers[profileName].Remove(playerName);
                EmitSignal(SignalName.PlayerLeft, profileName, playerName);
            }
        }
        else if (message.Contains("Done ("))
        {
            EmitSignal(SignalName.StatusChanged, profileName, "Running");
        }

        // Crash/Conflict Detection
        CheckForModConflicts(profileName, message);
    }

    // Accumulate detected conflicts to batch them
    private Dictionary<string, List<(string modName, string fileName)>> _pendingConflicts = new();
    private Dictionary<string, bool> _conflictDialogShown = new();

    private void CheckForModConflicts(string profileName, string message)
    {
        // Initialize tracking for this profile
        if (!_pendingConflicts.ContainsKey(profileName))
            _pendingConflicts[profileName] = new List<(string, string)>();
        if (!_conflictDialogShown.ContainsKey(profileName))
            _conflictDialogShown[profileName] = false;

        // If we already showed dialog for this session, skip
        if (_conflictDialogShown[profileName]) return;

        string modId = "";
        string modDisplayName = "";

        // Pattern 1: Forge crash summary format "ModName (modid) has failed to load correctly"
        // This is the most reliable pattern from the crash report
        var crashSummaryMatch = Regex.Match(message, @"(\w[\w\s]*?)\s+\((\w+)\)\s+has failed to load correctly");
        if (crashSummaryMatch.Success)
        {
            modDisplayName = crashSummaryMatch.Groups[1].Value.Trim();
            modId = crashSummaryMatch.Groups[2].Value;
        }
        // Pattern 2: "Failed to create mod instance. ModID: xxx"
        else if (message.Contains("Failed to create mod instance"))
        {
            var modIdMatch = Regex.Match(message, @"ModID:\s*(\w+)");
            if (modIdMatch.Success)
            {
                modId = modIdMatch.Groups[1].Value;
                modDisplayName = modId;
            }
        }
        // Pattern 3: WATERMeDIA specific - very clear message
        else if (message.Contains("WATERMeDIA") && (message.Contains("SERVER-SIDE") || message.Contains("server-side")))
        {
            modId = "watermedia";
            modDisplayName = "WaterMedia";
        }
        // Pattern 4: "invalid dist DEDICATED_SERVER" with mod file path
        else if (message.Contains("invalid dist DEDICATED_SERVER"))
        {
            // Try to extract from Mod File path in the message
            var jarPathMatch = Regex.Match(message, @"mods[/\\]([^/\\]+\.jar)");
            if (jarPathMatch.Success)
            {
                string jarName = jarPathMatch.Groups[1].Value;
                modId = Regex.Replace(jarName, @"[-_][\d\.mc]+.*\.jar$", "", RegexOptions.IgnoreCase);
                modDisplayName = modId;
            }
        }

        // If we found a mod conflict
        if (!string.IsNullOrEmpty(modId))
        {
            // Check if we already have this mod in pending
            if (_pendingConflicts[profileName].Any(c => c.modName.Equals(modId, StringComparison.OrdinalIgnoreCase)))
                return;

            string fileName = "";

            // Find the JAR file
            if (_serverPaths.TryGetValue(profileName, out string path))
            {
                string modsDir = Path.Combine(path, "mods");
                if (Directory.Exists(modsDir))
                {
                    var jars = Directory.GetFiles(modsDir, "*.jar");
                    var matchingJar = jars.FirstOrDefault(f =>
                        Path.GetFileName(f).ToLower().Contains(modId.ToLower()));
                    if (matchingJar != null) fileName = Path.GetFileName(matchingJar);
                }
            }

            _pendingConflicts[profileName].Add((modDisplayName, fileName));
            GD.Print($"[ModConflict] Detected: {modDisplayName} -> {fileName}");
        }

        // Check for the final crash indicator to emit all pending conflicts
        if ((message.Contains("Failed to complete lifecycle event CONSTRUCT") ||
             message.Contains("Crash report saved to") ||
             message.Contains("Failed to start the minecraft server")) &&
            _pendingConflicts[profileName].Count > 0)
        {
            var conflicts = _pendingConflicts[profileName];
            var modNames = conflicts.Select(c => c.modName).ToArray();
            var fileNames = conflicts.Select(c => c.fileName).ToArray();

            GD.Print($"[ModConflict] Emitting signal with {conflicts.Count} conflicts");
            _conflictDialogShown[profileName] = true;
            EmitSignal(SignalName.ModConflictDetected, profileName, modNames, fileNames);

            // Clear for next run
            _pendingConflicts[profileName].Clear();
        }
    }

    private string ExtractPlayerName(string log)
    {
        try
        {
            int infoIndex = log.IndexOf("INFO]: ");
            if (infoIndex != -1)
            {
                // Simple parser - assumes standard "Player joined the game"
                // [12:34:56] [Server thread/INFO]: Kael joined the game
                string afterInfo = log.Substring(infoIndex + "INFO]: ".Length).Trim();
                if (afterInfo.Contains("joined the game"))
                {
                    return afterInfo.Replace(" joined the game", "").Trim();
                }
                else if (afterInfo.Contains("left the game"))
                {
                    return afterInfo.Replace(" left the game", "").Trim();
                }
            }
        }
        catch { }
        return null;
    }

    // This method is no longer used as MaxRamMb is removed and RAM parsing is done locally in StartServer.
    // private float ParseRamToMb(string ramStr)
    // {
    //     if (string.IsNullOrEmpty(ramStr)) return 4096;
    //     ramStr = ramStr.ToUpper().Trim();

    //     try
    //     {
    //         if (ramStr.EndsWith("G")) return float.Parse(ramStr.TrimEnd('G')) * 1024f;
    //         if (ramStr.EndsWith("M")) return float.Parse(ramStr.TrimEnd('M'));
    //         if (float.TryParse(ramStr, out float val)) return val; // Assume MB if number
    //     }
    //     catch { return 4096; }
    //     return 4096;
    // }

    [SupportedOSPlatform("windows")]
    public void SetSmartAffinity(string profileName)
    {
        SetManualAffinity(profileName, AffinityHelper.GetSmartMask());
    }

    [SupportedOSPlatform("windows")]
    public void SetManualAffinity(string profileName, long mask)
    {
        if (IsRunning(profileName) && _serverProcesses.TryGetValue(profileName, out var process))
        {
            try
            {
                if (mask <= 0) return;
                process.ProcessorAffinity = (IntPtr)mask;
                EmitSignal(SignalName.LogReceived, profileName, $"[System] CPU Affinity set (Mask: {mask})", false);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to set CPU affinity for {profileName}: " + ex.Message);
            }
        }
    }
    private void KillZombieProcesses(string profileName, string serverPath)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // Normalize path for PowerShell comparison
            string normalizedPath = Path.GetFullPath(serverPath).Replace("\\", "\\\\");

            // Use PowerShell to find Java processes where the command line or working directory involves this server path
            // This is safer than just killing all 'java.exe'
            string script = $"Get-CimInstance Win32_Process -Filter \"Name = 'java.exe'\" | " +
                            $"Where-Object {{ $_.CommandLine -like '*{normalizedPath}*' }} | " +
                            $"ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var p = Process.Start(psi);
            p?.WaitForExit(3000); // Give it a few seconds
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[System] Failed to check for zombie processes: {ex.Message}");
        }
    }

    private int ParseRamToMb(string ramStr)
    {
        if (string.IsNullOrWhiteSpace(ramStr)) return 2048;
        ramStr = ramStr.ToUpper().Trim();
        try
        {
            if (ramStr.EndsWith("G")) return (int)(float.Parse(ramStr.Replace("G", "")) * 1024);
            if (ramStr.EndsWith("M")) return int.Parse(ramStr.Replace("M", ""));
            return int.Parse(ramStr);
        }
        catch { return 2048; }
    }
}
