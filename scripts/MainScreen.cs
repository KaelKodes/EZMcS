using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public partial class MainScreen : Control
{
    private RichTextLabel _consoleOutput;
    private LineEdit _commandInput;
    private ItemList _playerList;
    private Label _statusLabel;
    private Button _startStopButton;

    // Setup Panel
    private VBoxContainer _setupPanel;
    private LineEdit _pathInput;
    private LineEdit _jarInput;
    private Button _analyzeButton;
    private LineEdit _maxRamInput;
    private LineEdit _minRamInput;
    private LineEdit _javaInput;
    private Button _flagsButton;

    // Monitor Panel
    private VBoxContainer _monitorPanel;
    private Label _restartNotification;
    private Button _configButton;
    private Button _modpackButton;

    // Profiles
    private OptionButton _profileSelector;
    private Button _saveProfileButton;
    private List<ProfileManager.ServerProfile> _profiles = new List<ProfileManager.ServerProfile>();

    // Networking
    private OptionButton _remoteMode;
    private LineEdit _remoteAddress;
    private LineEdit _remotePort;
    private Button _networkButton;

    // Sub-scenes
    private ConfigEditor _configEditor;
    private ModpackDownloader _modpackDownloader;
    private FlagsEditor _flagsEditor;
    private PopupMenu _playerMenu;
    private string _selectedPlayer;

    public override void _Ready()
    {
        _consoleOutput = GetNode<RichTextLabel>("%ConsoleOutput");
        _commandInput = GetNode<LineEdit>("%CommandInput");
        _playerList = GetNode<ItemList>("%PlayerList");

        _setupPanel = GetNode<VBoxContainer>("%SetupPanel");
        _pathInput = GetNode<LineEdit>("%PathInput");
        _jarInput = GetNode<LineEdit>("%JarInput");
        _analyzeButton = GetNode<Button>("%AnalyzeButton");
        _maxRamInput = GetNode<LineEdit>("%MaxRamInput");
        _minRamInput = GetNode<LineEdit>("%MinRamInput");
        _javaInput = GetNode<LineEdit>("%JavaInput");
        _flagsButton = GetNode<Button>("%FlagsButton");

        _monitorPanel = GetNode<VBoxContainer>("%MonitorPanel");
        _statusLabel = GetNode<Label>("%StatusLabel");
        _restartNotification = GetNode<Label>("%RestartNotification");
        _startStopButton = GetNode<Button>("%StartStopButton");
        _configButton = GetNode<Button>("%ConfigButton");
        _modpackButton = GetNode<Button>("%ModpackButton");

        _profileSelector = GetNode<OptionButton>("%ProfileSelector");
        _saveProfileButton = GetNode<Button>("%SaveProfileButton");

        _remoteMode = GetNode<OptionButton>("%RemoteMode");
        _remoteAddress = GetNode<LineEdit>("%RemoteAddress");
        _remotePort = GetNode<LineEdit>("%RemotePort");
        _networkButton = GetNode<Button>("%NetworkButton");

        _configEditor = GetNode<ConfigEditor>("%ConfigEditor");
        _modpackDownloader = GetNode<ModpackDownloader>("%ModpackDownloader");
        _flagsEditor = GetNode<FlagsEditor>("%FlagsEditor");
        _playerMenu = GetNode<PopupMenu>("%PlayerMenu");

        InitializeSignals();
        RefreshProfiles();
        UpdateUIState(false);
    }

    private void InitializeSignals()
    {
        ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
        sm.LogReceived += OnLogReceived;
        sm.StatusChanged += OnStatusChanged;
        sm.PlayerJoined += OnPlayerJoined;
        sm.PlayerLeft += OnPlayerLeft;

        _commandInput.TextSubmitted += OnCommandSubmitted;
        _networkButton.Pressed += OnNetworkButtonPressed;
        _configButton.Pressed += OnConfigButtonPressed;
        _modpackButton.Pressed += OnModpackButtonPressed;
        _flagsButton.Pressed += OnFlagsButtonPressed;
        _analyzeButton.Pressed += OnAnalyzeButtonPressed;
        _saveProfileButton.Pressed += OnSaveProfilePressed;
        _profileSelector.ItemSelected += OnProfileSelected;

        _playerList.ItemActivated += OnPlayerItemActivated;
        _playerList.ItemClicked += OnPlayerItemClicked;
        _playerMenu.IdPressed += OnPlayerMenuIdPressed;

        _configEditor.PropertiesSaved += OnConfigPropertiesSaved;
        _flagsEditor.FlagsSaved += OnFlagsSaved;
        _startStopButton.Pressed += _on_start_stop_button_pressed;

        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        nm.ConnectionStatusChanged += OnNetworkStatusChanged;
        nm.ConfigurationSynced += OnConfigurationSynced;
        nm.Multiplayer.PeerConnected += OnRemotePeerConnected;
    }

    private void RefreshProfiles()
    {
        _profiles = ProfileManager.LoadProfiles();
        _profileSelector.Clear();
        _profileSelector.AddItem("New Profile...");

        foreach (var p in _profiles.OrderByDescending(x => x.LastUsed))
        {
            _profileSelector.AddItem(p.Name);
        }
    }

    private void OnProfileSelected(long index)
    {
        if (index == 0) return; // New Profile

        string name = _profileSelector.GetItemText((int)index);
        var p = _profiles.FirstOrDefault(x => x.Name == name);
        if (p != null)
        {
            _pathInput.Text = p.Path;
            _jarInput.Text = p.Jar;
            _maxRamInput.Text = p.MaxRam;
            _minRamInput.Text = p.MinRam;
            _javaInput.Text = p.JavaPath;
            OnLogReceived($"[System] Loaded profile: {name}", false);
        }
    }

    private void OnSaveProfilePressed()
    {
        string path = _pathInput.Text;
        if (string.IsNullOrEmpty(path)) return;

        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = "My Server";

        var p = new ProfileManager.ServerProfile
        {
            Name = name,
            Path = _pathInput.Text,
            Jar = _jarInput.Text,
            MaxRam = _maxRamInput.Text,
            MinRam = _minRamInput.Text,
            JavaPath = _javaInput.Text
        };

        ProfileManager.SaveProfile(p);
        RefreshProfiles();

        // Select the newly saved one
        for (int i = 0; i < _profileSelector.ItemCount; i++)
        {
            if (_profileSelector.GetItemText(i) == name)
            {
                _profileSelector.Select(i);
                break;
            }
        }
    }

    private void OnAnalyzeButtonPressed()
    {
        string path = _pathInput.Text;
        string jar = _jarInput.Text;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(jar))
        {
            OnLogReceived("[System] Specify Path and JAR first.", true);
            return;
        }

        string fullPath = Path.Combine(path, jar);
        int req = JavaHelper.DetectRequiredJava(fullPath);
        if (req > 0)
        {
            OnLogReceived($"[System] Analysis complete. MC requires Java {req}.", false);
            string best = JavaHelper.GetBestJavaPath(req);
            if (best != "java")
            {
                _javaInput.Text = best;
                OnLogReceived($"[System] Automatically matched with: {best}", false);
            }
            else
            {
                OnLogReceived($"[System] Java {req} not found. Please install it or use manual path.", true);
            }
        }
        else
        {
            OnLogReceived("[System] Analysis failed. Ensure the JAR is valid.", true);
        }
    }

    private void UpdateUIState(bool running)
    {
        // Toggle the "Setup" panel based on whether the server is running
        // If running, we hide the clutter. If stopped, we show it.
        _setupPanel.Visible = !running;
        _profileSelector.Disabled = running;
        _saveProfileButton.Disabled = running;
    }

    private void OnStatusChanged(string newStatus)
    {
        _statusLabel.Text = $"Status: {newStatus}";
        bool running = (newStatus == "Running" || newStatus == "Starting" || newStatus == "Stopping");
        UpdateUIState(running);

        if (newStatus == "Running")
        {
            _statusLabel.SelfModulate = new Color(0.2f, 1.0f, 0.2f);
        }
        else if (newStatus == "Starting")
        {
            _statusLabel.SelfModulate = new Color(1.0f, 1.0f, 0.2f);
            _restartNotification.Visible = false;
        }
        else if (newStatus == "Stopped" || newStatus == "Killed")
        {
            _statusLabel.SelfModulate = new Color(1.0f, 0.2f, 0.2f);
            _startStopButton.Text = "Start Server";
            _startStopButton.Disabled = false;
        }
        else _statusLabel.SelfModulate = new Color(1.0f, 1.0f, 1.0f);

        if (running)
        {
            _startStopButton.Text = newStatus == "Stopping" ? "Stopping..." : "Stop Server";
            _startStopButton.Disabled = newStatus == "Stopping";
        }
    }

    // Remaining logic (Network, Logs, Commands, Players) stays similar but uses refined nodes...
    // [TRUNCATED for briefness as per original script logic]
    // I will include the full logic in the final write_to_file to ensure correctness.

    private void OnRemotePeerConnected(long id) { if (Multiplayer.IsServer()) SyncSettingsToNetwork(); }

    private void OnConfigurationSynced(string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags)
    {
        _pathInput.Text = path;
        _jarInput.Text = jar;
        _maxRamInput.Text = maxRam;
        _minRamInput.Text = minRam;
        _javaInput.Text = javaPath;
        OnLogReceived("[System] Configuration synced from host.", false);
    }

    private void SyncSettingsToNetwork()
    {
        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        if (nm.Multiplayer.IsServer())
        {
            nm.SyncConfigurationToAll(_pathInput.Text, _jarInput.Text, _maxRamInput.Text, _minRamInput.Text, _javaInput.Text, _flagsEditor.GetFormattedFlags());
        }
    }

    private void OnConfigPropertiesSaved()
    {
        if (GetNode<ServerManager>("/root/ServerManager").IsRunning) _restartNotification.Visible = true;
    }

    private void OnFlagsSaved()
    {
        if (GetNode<ServerManager>("/root/ServerManager").IsRunning) _restartNotification.Visible = true;
        SyncSettingsToNetwork();
    }

    private void OnLogReceived(string message, bool isError)
    {
        string timestamp = $"[color=#888888][{DateTime.Now:HH:mm:ss}][/color] ";
        string color = isError ? "#ff5555" : "#ffffff";
        if (message.Contains("[INFO]")) message = message.Replace("[INFO]", "[color=#55ff55][INFO][/color]");
        else if (message.Contains("[WARN]")) message = message.Replace("[WARN]", "[color=#ffff55][WARN][/color]");
        else if (message.Contains("[ERROR]")) message = message.Replace("[ERROR]", "[color=#ff5555][ERROR][/color]");
        _consoleOutput.AppendText($"{timestamp}[color={color}]{message}[/color]\n");
    }

    private void OnPlayerJoined(string playerName) { _playerList.AddItem(playerName); OnLogReceived($"[System] Player {playerName} joined.", false); }
    private void OnPlayerLeft(string playerName) { for (int i = 0; i < _playerList.ItemCount; i++) if (_playerList.GetItemText(i) == playerName) { _playerList.RemoveItem(i); break; } OnLogReceived($"[System] Player {playerName} left.", false); }

    private void OnCommandSubmitted(string command) { if (string.IsNullOrWhiteSpace(command)) return; SendCommand(command); _commandInput.Clear(); }

    private void SendCommand(string command)
    {
        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        if (nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer()) nm.Rpc(nameof(NetworkManager.SendRemoteCommand), command);
        else GetNode<ServerManager>("/root/ServerManager").SendCommand(command);
    }

    private void _on_start_stop_button_pressed()
    {
        ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        bool isClient = nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer();

        if (sm.IsRunning || (isClient && _statusLabel.Text.Contains("Running")))
        {
            if (isClient) nm.Rpc(nameof(NetworkManager.RequestStopServer));
            else sm.StopServer();
        }
        else
        {
            if (isClient) nm.Rpc(nameof(NetworkManager.RequestStartServer), _pathInput.Text, _jarInput.Text, _maxRamInput.Text, _minRamInput.Text, _javaInput.Text, _flagsEditor.GetFormattedFlags());
            else { sm.StartServer(_pathInput.Text, _jarInput.Text, _maxRamInput.Text, _minRamInput.Text, _javaInput.Text, _flagsEditor.GetFormattedFlags()); SyncSettingsToNetwork(); }
        }
    }

    private void OnNetworkButtonPressed()
    {
        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        if (nm.Multiplayer.MultiplayerPeer != null) { nm.Disconnect(); return; }
        int mode = _remoteMode.Selected;
        int port = 8181; int.TryParse(_remotePort.Text, out port);
        if (mode == 1) { nm.CreateHost(port); SyncSettingsToNetwork(); }
        else if (mode == 2) nm.ConnectToHost(_remoteAddress.Text ?? "127.0.0.1", port);
    }

    private void OnConfigButtonPressed() { if (string.IsNullOrEmpty(_pathInput.Text)) return; _configEditor.Open(_pathInput.Text); }
    private void OnFlagsButtonPressed() { if (string.IsNullOrEmpty(_pathInput.Text)) return; _flagsEditor.Open(_pathInput.Text); }
    private void OnModpackButtonPressed() { if (string.IsNullOrEmpty(_pathInput.Text)) return; _modpackDownloader.Open(_pathInput.Text); }

    private void OnNetworkStatusChanged(bool connected, bool isHost)
    {
        _networkButton.Text = connected ? (isHost ? "Stop Hosting" : "Disconnect") : "Initialize Network";
        _remoteMode.Disabled = connected;
        _remoteAddress.Editable = !connected;
        _remotePort.Editable = !connected;
        if (connected && !isHost)
        {
            _setupPanel.Visible = false;
            _profileSelector.Disabled = true;
            _statusLabel.Text = "Status: Connected (Syncing...)";
            _playerList.Clear();
        }
    }

    private void OnPlayerItemClicked(long index, Vector2 atPosition, long mouseButtonIndex)
    {
        if (mouseButtonIndex == (long)MouseButton.Right)
        {
            _selectedPlayer = _playerList.GetItemText((int)index);
            _playerMenu.Position = (Vector2I)GetGlobalMousePosition() + (Vector2I)GetWindow().Position;
            _playerMenu.Popup();
        }
    }

    private void OnPlayerItemActivated(long index) { _selectedPlayer = _playerList.GetItemText((int)index); _commandInput.Text = $"/msg {_selectedPlayer} "; _commandInput.GrabFocus(); _commandInput.CaretColumn = _commandInput.Text.Length; }
    private void OnPlayerMenuIdPressed(long id) { if (string.IsNullOrEmpty(_selectedPlayer)) return; switch (id) { case 0: SendCommand($"kick {_selectedPlayer}"); break; case 1: SendCommand($"ban {_selectedPlayer}"); break; case 2: SendCommand($"op {_selectedPlayer}"); break; } }
}
