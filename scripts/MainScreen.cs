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

	private ProgressBar _cpuBar;
	private ProgressBar _ramBar;
	private ProgressBar _serverRamBar;
	private SystemMonitor _systemMonitor;
	private CheckBox _smartAffinity;
	private LineEdit _modsInput;
	private Button _browseModsButton;
	private FileDialog _modsDialog;
	private AcceptDialog _affinityDialog;
	private GridContainer _coreGrid;
	private Button _optimizeManualButton;
	private Button _manualAffinityButton;
	private OptionButton _themeSelector;
	private Dictionary<int, Theme> _themeResources = new Dictionary<int, Theme>();

	private List<string> _commandHistory = new List<string>();
	private int _historyIndex = -1;

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

		_cpuBar = GetNode<ProgressBar>("%CpuBar");
		_ramBar = GetNode<ProgressBar>("%RamBar");
		_serverRamBar = GetNode<ProgressBar>("%ServerRamBar");
		_smartAffinity = GetNode<CheckBox>("%SmartAffinity");
		_modsInput = GetNode<LineEdit>("%ModsInput");
		_browseModsButton = GetNode<Button>("%BrowseModsButton");
		_modsDialog = GetNode<FileDialog>("%ModsDialog");
		_affinityDialog = GetNode<AcceptDialog>("%AffinityDialog");
		_coreGrid = GetNode<GridContainer>("%CoreGrid");
		_optimizeManualButton = GetNode<Button>("%OptimizeManualButton");
		_manualAffinityButton = GetNode<Button>("%ManualAffinityButton");
		_themeSelector = GetNode<OptionButton>("%ThemeDrop");

		// Add SystemMonitor dynamically
		_systemMonitor = new SystemMonitor();
		AddChild(_systemMonitor);

		InitializeSignals();
		InitializeThemes();
		RefreshProfiles();
		UpdateUIState(false);
		SetSystemMonitorVisibility(false); // Hidden by default

		_commandInput.GrabFocus();
		_commandInput.GuiInput += OnCommandInputGuiInput;

		// Prevent other UI from taking focus via keyboard
		SetAllFocusModes(this, FocusModeEnum.None);
		_commandInput.FocusMode = FocusModeEnum.All;
		_consoleOutput.FocusMode = FocusModeEnum.Click;
	}

	private void SetAllFocusModes(Node node, FocusModeEnum mode)
	{
		if (node is Control control && control != _commandInput && control != _consoleOutput)
		{
			// Allow clicking to focus LineEdits and Buttons, but not arrow-key navigation
			if (control is LineEdit || control is Button || control is CheckBox || control is ItemList || control is OptionButton)
			{
				control.FocusMode = FocusModeEnum.Click;
			}
			else
			{
				control.FocusMode = mode;
			}
		}
		foreach (Node child in node.GetChildren())
		{
			SetAllFocusModes(child, mode);
		}
	}

	private void OnCommandInputGuiInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Up)
			{
				if (_commandHistory.Count > 0)
				{
					_historyIndex++;
					if (_historyIndex >= _commandHistory.Count) _historyIndex = _commandHistory.Count - 1;
					_commandInput.Text = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
					_commandInput.CaretColumn = _commandInput.Text.Length;
				}
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Key.Down)
			{
				_historyIndex--;
				if (_historyIndex < -1) _historyIndex = -1;

				if (_historyIndex == -1)
				{
					_commandInput.Text = "";
				}
				else
				{
					_commandInput.Text = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
				}
				_commandInput.CaretColumn = _commandInput.Text.Length;
				GetViewport().SetInputAsHandled();
			}
		}
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
		nm.RemotePropertiesReceived += _configEditor.HandleRemoteProperties;
		nm.Multiplayer.PeerConnected += OnRemotePeerConnected;
		nm.SystemStatsReceived += OnSystemStatsReceived;

		_systemMonitor.StatsUpdated += OnLocalStatsUpdated;

		_browseModsButton.Pressed += () => _modsDialog.PopupCentered();
		_modsDialog.DirSelected += (path) => _modsInput.Text = path;
		_optimizeManualButton.Pressed += OnOptimizeManualPressed;
		_manualAffinityButton.Pressed += ShowAffinityPicker;
		_themeSelector.ItemSelected += OnThemeSelected;

		InitializeCoreGrid();
	}

	private void InitializeCoreGrid()
	{
		foreach (var child in _coreGrid.GetChildren()) child.QueueFree();

		var topology = AffinityHelper.GetCoreTopology();
		string lastGroup = "";

		foreach (var core in topology)
		{
			if (core.GroupName != lastGroup)
			{
				// Add a group header span
				var separator = new HSeparator { CustomMinimumSize = new Vector2(0, 10) };
				_coreGrid.AddChild(separator);

				var label = new Label
				{
					Text = core.GroupName,
					ThemeTypeVariation = "HeaderSmall",
					Modulate = new Color(0.7f, 0.7f, 1.0f)
				};
				_coreGrid.AddChild(label);

				// Add two spacers to fill the 4-column grid row for the header
				_coreGrid.AddChild(new Control());
				_coreGrid.AddChild(new Control());

				lastGroup = core.GroupName;
			}

			string suffix = core.IsLogical ? " (L)" : " (P)";
			if (core.Type == "E-Core") suffix = ""; // E-cores don't have L/P distinction usually

			var cb = new CheckBox
			{
				Text = $"#{core.Index}{suffix}",
				Name = $"Core{core.Index}",
				TooltipText = $"{core.Type} thread"
			};

			// Dim logical threads slightly to make physical cores stand out
			if (core.IsLogical) cb.Modulate = new Color(0.8f, 0.8f, 0.8f, 0.7f);

			_coreGrid.AddChild(cb);
		}
	}

	private void ShowAffinityPicker()
	{
		_affinityDialog.PopupCentered();
	}

	private void OnOptimizeManualPressed()
	{
		long smartMask = AffinityHelper.GetSmartMask();
		SetCoreGridFromMask(smartMask);
		OnLogReceived("[System] Manual grid populated with optimized physical core mask.", false);
	}

	private long GetManualAffinityMask()
	{
		long mask = 0;
		int i = 0;
		foreach (Node child in _coreGrid.GetChildren())
		{
			if (child is CheckBox cb && cb.ButtonPressed)
			{
				mask |= (1L << i);
			}
			i++;
		}
		return mask == 0 ? -1 : mask; // -1 means use smart or all
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
			_modsInput.Text = p.ModsPath;
			_flagsEditor.SetServerPath(p.Path);
			_smartAffinity.ButtonPressed = p.UseSmartAffinity;
			SetCoreGridFromMask(p.AffinityMask);

			OnLogReceived($"[System] Loaded profile: {name}", false);
		}
	}

	private void SetCoreGridFromMask(long mask)
	{
		InitializeCoreGrid(); // Ensure grid exists
		if (mask == -1) return;

		for (int i = 0; i < _coreGrid.GetChildCount(); i++)
		{
			if (_coreGrid.GetChild(i) is CheckBox cb)
			{
				cb.ButtonPressed = (mask & (1L << i)) != 0;
			}
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
			JavaPath = _javaInput.Text,
			ModsPath = _modsInput.Text,
			JvmFlags = _flagsEditor.GetFormattedFlags(),
			UseSmartAffinity = _smartAffinity.ButtonPressed,
			AffinityMask = GetManualAffinityMask()
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
		// Selectively hide clutter while keeping RAM and Flags visible
		_pathInput.GetParent().GetChild<Label>(_pathInput.GetIndex() - 1).Visible = !running; // PathLabel
		_pathInput.Visible = !running;

		Node hboxJar = GetNode("%AnalyzeButton").GetParent();
		hboxJar.GetParent().GetChild<Label>(hboxJar.GetIndex() - 1).Visible = !running; // JarLabel
		((Control)hboxJar).Visible = !running;

		_javaInput.GetParent().GetChild<Label>(_javaInput.GetIndex() - 1).Visible = !running; // JavaLabel
		_javaInput.Visible = !running;

		// Keep the main panel visible so RAM and Flags can be seen
		_setupPanel.Visible = true;
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

	private void OnCommandSubmitted(string command)
	{
		if (string.IsNullOrWhiteSpace(command)) return;

		// Add to history if unique or different from last
		if (_commandHistory.Count == 0 || _commandHistory[_commandHistory.Count - 1] != command)
		{
			_commandHistory.Add(command);
			if (_commandHistory.Count > 50) _commandHistory.RemoveAt(0); // Cap history
		}

		_historyIndex = -1;
		SendCommand(command);
		_commandInput.Clear();
	}

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

		// Use the label text to determine the perceived state if in client mode
		bool perceivedRunning = isClient ? (_statusLabel.Text.Contains("Running") || _statusLabel.Text.Contains("Starting")) : sm.IsRunning;

		if (perceivedRunning)
		{
			if (isClient) nm.Rpc(nameof(NetworkManager.RequestStopServer));
			else sm.StopServer();
		}
		else
		{
			if (isClient) nm.Rpc(nameof(NetworkManager.RequestStartServer), _pathInput.Text, _jarInput.Text, _maxRamInput.Text, _minRamInput.Text, _javaInput.Text, _flagsEditor.GetFormattedFlags());
			else
			{
				sm.StartServer(_pathInput.Text, _jarInput.Text, _maxRamInput.Text, _minRamInput.Text, _javaInput.Text, _flagsEditor.GetFormattedFlags(), _modsInput.Text);
				SyncSettingsToNetwork();

				// Apply Affinity
				GetTree().CreateTimer(0.5f).Connect("timeout", Callable.From(() =>
				{
					if (_smartAffinity.ButtonPressed) sm.SetSmartAffinity();
					else
					{
						long mask = GetManualAffinityMask();
						if (mask != -1) sm.SetManualAffinity(mask);
					}
				}));
			}
		}
	}

	private void OnNetworkButtonPressed()
	{
		NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
		if (nm.Multiplayer.MultiplayerPeer != null) { nm.Disconnect(); return; }
		int mode = _remoteMode.Selected;
		int port = 8181; int.TryParse(_remotePort.Text, out port);
		if (mode == 1)
		{
			nm.CreateHost(port);
			SyncSettingsToNetwork();
			SetSystemMonitorVisibility(true); // Show local stats when hosting
		}
		else if (mode == 2)
		{
			nm.ConnectToHost(_remoteAddress.Text ?? "127.0.0.1", port);
			SetSystemMonitorVisibility(true); // Show remote stats when connected
		}
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

		if (connected)
		{
			SetSystemMonitorVisibility(true);
			if (!isHost)
			{
				UpdateUIState(true); // Hide clutter but keep RAM visible for clients
				_profileSelector.Disabled = true;
				_statusLabel.Text = "Status: Connected (Syncing...)";
				_statusLabel.SelfModulate = new Color(1, 1, 1);
				_playerList.Clear();
				_consoleOutput.Clear();

				// Disable start/stop button until we receive the real state from host
				_startStopButton.Disabled = true;
				_startStopButton.Text = "Syncing...";
			}
		}
		else
		{
			SetSystemMonitorVisibility(false);
			ResetUI();
		}
	}

	private void SetSystemMonitorVisibility(bool visible)
	{
		// Hide/Show the HBoxContainers (CpuBox and RamBox)
		// Casting the parent Node back to Control to access Visible property
		if (_cpuBar.GetParent() is Control cpuBox) cpuBox.Visible = visible;
		if (_ramBar.GetParent() is Control ramBox) ramBox.Visible = visible;
		if (_serverRamBar.GetParent() is Control serverRamBox) serverRamBox.Visible = visible;

		// Hide/Show the "System Monitor:" Label which is above the boxes
		// MonitorPanel is the parent of the box (CpuBox).
		Node monitorPanel = _cpuBar.GetParent().GetParent();

		// The label is one index before the CpuBox.
		int cpuIndex = _cpuBar.GetParent().GetIndex();
		if (cpuIndex > 0)
		{
			Node systemLabel = monitorPanel.GetChild(cpuIndex - 1);
			if (systemLabel is Control ctrl)
			{
				ctrl.Visible = visible;
			}
		}
	}

	private void ResetUI()
	{
		_setupPanel.Visible = true;
		_profileSelector.Disabled = false;
		_restartNotification.Visible = false;

		ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
		if (!sm.IsRunning)
		{
			_statusLabel.Text = "Status: Stopped";
			_statusLabel.SelfModulate = new Color(1, 0.2f, 0.2f);
			_playerList.Clear();
			_consoleOutput.Clear();
			_startStopButton.Text = "Start Server";
			_startStopButton.Disabled = false;
		}
		else
		{
			// Server is still running locally as host
			_statusLabel.Text = "Status: Running";
			_statusLabel.SelfModulate = new Color(0.2f, 1.0f, 0.2f);
			_startStopButton.Text = "Stop Server";
			_startStopButton.Disabled = false;
		}

		RefreshProfiles();
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

	private void OnLocalStatsUpdated(float cpu, float ram, float serverRam)
	{
		NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
		bool isClient = nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer();

		// If we are a client, we want to see the HOST's stats, not our own local ones.
		if (isClient) return;

		_cpuBar.Value = cpu;
		_ramBar.Value = ram;
		_serverRamBar.Value = serverRam;

		// If hosting, broadcast to clients
		if (nm.Multiplayer.IsServer())
		{
			nm.Rpc(nameof(NetworkManager.ReceiveRemoteSystemStats), cpu, ram, serverRam);
		}
	}

	private void OnSystemStatsReceived(float cpu, float ram, float serverRam)
	{
		_cpuBar.Value = cpu;
		_ramBar.Value = ram;
		_serverRamBar.Value = serverRam;
	}

	private void OnPlayerMenuIdPressed(long id) { if (string.IsNullOrEmpty(_selectedPlayer)) return; switch (id) { case 0: SendCommand($"kick {_selectedPlayer}"); break; case 1: SendCommand($"ban {_selectedPlayer}"); break; case 2: SendCommand($"op {_selectedPlayer}"); break; } }

	private void InitializeThemes()
	{
		_themeResources[0] = GD.Load<Theme>("res://resources/main_theme.tres");
		_themeResources[1] = GD.Load<Theme>("res://resources/godot_theme.tres");
		_themeResources[2] = GD.Load<Theme>("res://resources/fantasy_theme.tres");
		_themeResources[3] = GD.Load<Theme>("res://resources/cyberpunk_theme.tres");
	}

	private void OnThemeSelected(long index)
	{
		if (_themeResources.ContainsKey((int)index))
		{
			Theme selectedTheme = _themeResources[(int)index];
			Theme = selectedTheme;

			// Propagate to windows
			_configEditor.Theme = selectedTheme;
			_modpackDownloader.Theme = selectedTheme;
			_flagsEditor.Theme = selectedTheme;

			// Propagate to popups/dialogs
			_playerMenu.Theme = selectedTheme;
			_affinityDialog.Theme = selectedTheme;
			_modsDialog.Theme = selectedTheme;
		}
	}
}
