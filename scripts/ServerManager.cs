using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public partial class ServerManager : Node
{
    [Signal] public delegate void LogReceivedEventHandler(string message, bool isError);
    [Signal] public delegate void StatusChangedEventHandler(string newStatus);
    [Signal] public delegate void PlayerJoinedEventHandler(string playerName);
    [Signal] public delegate void PlayerLeftEventHandler(string playerName);

    private Process _serverProcess;
    private string _serverPath;
    private string _serverJar;
    private string _javaPath = "java";
    private string _maxRam = "4G";
    private string _minRam = "2G";
    private List<string> _onlinePlayers = new List<string>();

    public ReadOnlyCollection<string> OnlinePlayers => _onlinePlayers.AsReadOnly();
    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    public void StartServer(string path, string jar, string maxRam = "4G", string minRam = "2G", string javaPath = "", string extraFlags = "")
    {
        if (IsRunning) return;

        _serverPath = path;
        _serverJar = jar;
        _maxRam = maxRam;
        _minRam = minRam;

        // Intelligent Java Detection
        if (string.IsNullOrWhiteSpace(javaPath) || javaPath.ToLower() == "auto" || javaPath == "Default (java)")
        {
            string fullJarPath = Path.Combine(path, jar);
            int requiredVersion = JavaHelper.DetectRequiredJava(fullJarPath);
            if (requiredVersion > 0)
            {
                EmitSignal(SignalName.LogReceived, $"[System] Detected Minecraft requirement: Java {requiredVersion}", false);
                _javaPath = JavaHelper.GetBestJavaPath(requiredVersion);
                EmitSignal(SignalName.LogReceived, $"[System] Using Java Path: {_javaPath}", false);
            }
            else
            {
                _javaPath = "java";
            }
        }
        else
        {
            _javaPath = javaPath;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _javaPath,
            Arguments = $"-Xmx{_maxRam} -Xms{_minRam} {extraFlags} -jar \"{_serverJar}\" nogui",
            WorkingDirectory = _serverPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _serverProcess = new Process { StartInfo = startInfo };
        _serverProcess.EnableRaisingEvents = true;
        _serverProcess.OutputDataReceived += (s, e) => { if (e.Data != null) CallDeferred(MethodName.HandleLog, e.Data, false); };
        _serverProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) CallDeferred(MethodName.HandleLog, e.Data, true); };
        _serverProcess.Exited += (s, e) => CallDeferred(MethodName.HandleExit);

        try
        {
            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
            EmitSignal(SignalName.StatusChanged, "Starting");
        }
        catch (Exception ex)
        {
            GD.PrintErr("Failed to start Minecraft server: " + ex.Message);
            EmitSignal(SignalName.LogReceived, "Failed to start: " + ex.Message, true);
            EmitSignal(SignalName.StatusChanged, "Stopped");
        }
    }

    public void SendCommand(string command)
    {
        if (IsRunning)
        {
            _serverProcess.StandardInput.WriteLine(command);
            EmitSignal(SignalName.LogReceived, $"> {command}", false);
        }
    }

    public void StopServer()
    {
        if (IsRunning)
        {
            SendCommand("stop");
            EmitSignal(SignalName.StatusChanged, "Stopping");
        }
    }

    public void KillServer()
    {
        if (IsRunning)
        {
            _serverProcess.Kill();
            EmitSignal(SignalName.StatusChanged, "Killed");
        }
    }

    private void HandleLog(string message, bool isError)
    {
        EmitSignal(SignalName.LogReceived, message, isError);
        ParseLog(message);
    }

    private void HandleExit()
    {
        EmitSignal(SignalName.StatusChanged, "Stopped");
        _serverProcess = null;
        _onlinePlayers.Clear();

        // EULA Check
        CheckEula();
    }

    private void CheckEula()
    {
        string eulaPath = Path.Combine(_serverPath, "eula.txt");
        if (File.Exists(eulaPath))
        {
            string content = File.ReadAllText(eulaPath);
            if (content.Contains("eula=false"))
            {
                EmitSignal(SignalName.LogReceived, "[System] Server stopped because EULA is not accepted. You can fix this in the 'Edit Server Config' menu or by editing eula.txt.", true);
            }
        }
    }

    public void AcceptEula()
    {
        string eulaPath = Path.Combine(_serverPath, "eula.txt");
        if (File.Exists(eulaPath))
        {
            string content = File.ReadAllText(eulaPath);
            content = content.Replace("eula=false", "eula=true");
            File.WriteAllText(eulaPath, content);
            EmitSignal(SignalName.LogReceived, "[System] EULA accepted.", false);
        }
        else
        {
            File.WriteAllLines(eulaPath, new string[] { "# Minecraft EULA", "eula=true" });
            EmitSignal(SignalName.LogReceived, "[System] eula.txt created and EULA accepted.", false);
        }
    }

    private void ParseLog(string message)
    {
        if (message.Contains("joined the game"))
        {
            string playerName = ExtractPlayerName(message);
            if (!string.IsNullOrEmpty(playerName))
            {
                if (!_onlinePlayers.Contains(playerName)) _onlinePlayers.Add(playerName);
                EmitSignal(SignalName.PlayerJoined, playerName);
            }
        }
        else if (message.Contains("left the game"))
        {
            string playerName = ExtractPlayerName(message);
            if (!string.IsNullOrEmpty(playerName))
            {
                _onlinePlayers.Remove(playerName);
                EmitSignal(SignalName.PlayerLeft, playerName);
            }
        }
        else if (message.Contains("Done ("))
        {
            EmitSignal(SignalName.StatusChanged, "Running");
        }
    }

    private string ExtractPlayerName(string log)
    {
        try
        {
            int infoIndex = log.IndexOf("INFO]: ");
            if (infoIndex != -1)
            {
                string rest = log.Substring(infoIndex + 7);
                return rest.Split(' ')[0];
            }
        }
        catch { }
        return null;
    }
}
