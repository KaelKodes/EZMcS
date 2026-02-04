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
	private string _currentPath = "";
	private string _currentJar = "";
	private string _currentJava = "";
	private LineEdit _maxRamInput;
	private LineEdit _minRamInput;
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
	// Per-server settings (stored in profile, no longer visible on main screen)
	private string _currentModsPath = "";
	private bool _useSmartAffinity = true;
	private long _currentAffinityMask = -1;

	// Dialogs
	private FileDialog _modsDialog;
	private AcceptDialog _affinityDialog;
	private GridContainer _coreGrid;
	private Button _optimizeManualButton;
	private OptionButton _themeSelector;
	private Dictionary<int, Theme> _themeResources = new Dictionary<int, Theme>();
	private Button _newServerButton;
	private Button _addExistingButton;
	private Button _deleteProfileButton;
	private ItemList _serverList;

	private ServerSetupWizard _setupWizard;
	private DependencyManager _dependencyManager;
	private DependencyDownloader _dependencyDownloader;
	private ServerSetupUI _serverSetupUI;
	private AddExistingUI _addExistingUI;
	private string _currentProfileName;

	// Server list context menu
	private PopupMenu _serverContextMenu;
	private string _rightClickedServerName;
	private ConfirmationDialog _deleteConfirmDialog;
	private LineEdit _deleteConfirmInput;
	private LineEdit _deleteConfirmCopy;
	private Window _cloneDialog;
	private LineEdit _cloneNameInput;
	private Window _serverConfigDialog;

	private List<string> _commandHistory = new List<string>();
	private int _historyIndex = -1;
	private ModConflictDialog _modConflictDialog;


	public override void _Ready()
	{
		// Console
		_consoleOutput = GetNode<RichTextLabel>("%ConsoleOutput");
		_commandInput = GetNode<LineEdit>("%CommandInput");

		// Sidebar
		_serverList = GetNode<ItemList>("%ServerList");
		_newServerButton = GetNode<Button>("%NewServerButton");
		_addExistingButton = GetNode<Button>("%AddExistingButton");

		// Setup Panel
		_setupPanel = GetNode<VBoxContainer>("%SetupPanel");
		_profileSelector = GetNode<OptionButton>("%ProfileSelector");
		_saveProfileButton = GetNode<Button>("%SaveProfileButton");
		_deleteProfileButton = GetNode<Button>("%DeleteProfileButton");
		_playerList = GetNode<ItemList>("%PlayerList");

		// Monitor Panel
		_monitorPanel = GetNode<VBoxContainer>("%MonitorPanel");
		_startStopButton = GetNode<Button>("%StartStopButton");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_restartNotification = GetNode<Label>("%RestartNotification");
		_maxRamInput = GetNode<LineEdit>("%MaxRamInput");
		_minRamInput = GetNode<LineEdit>("%MinRamInput");
		_configButton = GetNode<Button>("%ConfigButton");
		_modpackButton = GetNode<Button>("%ModpackButton");
		_flagsButton = GetNode<Button>("%FlagsButton");

		// Network
		_remoteMode = GetNode<OptionButton>("%RemoteMode");
		_remoteAddress = GetNode<LineEdit>("%RemoteAddress");
		_remotePort = GetNode<LineEdit>("%RemotePort");
		_networkButton = GetNode<Button>("%NetworkButton");

		// System Monitor
		_cpuBar = GetNode<ProgressBar>("%CpuBar");
		_ramBar = GetNode<ProgressBar>("%RamBar");
		_serverRamBar = GetNode<ProgressBar>("%ServerRamBar");
		_themeSelector = GetNode<OptionButton>("%ThemeDrop");

		// Sub-scenes and dialogs
		_configEditor = GetNode<ConfigEditor>("%ConfigEditor");
		_modpackDownloader = GetNode<ModpackDownloader>("%ModpackDownloader");
		_flagsEditor = GetNode<FlagsEditor>("%FlagsEditor");
		_playerMenu = GetNode<PopupMenu>("%PlayerMenu");
		_modsDialog = GetNode<FileDialog>("%ModsDialog");
		_affinityDialog = GetNode<AcceptDialog>("%AffinityDialog");
		_coreGrid = GetNode<GridContainer>("%CoreGrid");
		_optimizeManualButton = GetNode<Button>("%OptimizeManualButton");

		// Add SystemMonitor dynamically (not in scene)
		_systemMonitor = new SystemMonitor();
		AddChild(_systemMonitor);

		_setupWizard = new ServerSetupWizard();
		_setupWizard.Name = "ServerSetupWizard";
		AddChild(_setupWizard);

		_dependencyManager = new DependencyManager();
		_dependencyManager.Name = "DependencyManager";
		AddChild(_dependencyManager);

		_dependencyDownloader = new DependencyDownloader();
		_dependencyDownloader.Name = "DependencyDownloader";
		_dependencyDownloader.Visible = false;
		_dependencyDownloader.Setup(_dependencyManager);
		AddChild(_dependencyDownloader);

		_serverSetupUI = new ServerSetupUI();
		_serverSetupUI.Name = "ServerSetupUI";
		_serverSetupUI.Visible = false;
		_serverSetupUI.Setup(_setupWizard, _dependencyManager, this);
		AddChild(_serverSetupUI);

		_addExistingUI = new AddExistingUI();
		_addExistingUI.Name = "AddExistingUI";
		_addExistingUI.Visible = false;
		_addExistingUI.Setup(this);
		AddChild(_addExistingUI);

		_modConflictDialog = new ModConflictDialog();
		_modConflictDialog.Name = "ModConflictDialog";
		AddChild(_modConflictDialog);

		// Server context menu (right-click on sidebar)
		_serverContextMenu = new PopupMenu();
		_serverContextMenu.Name = "ServerContextMenu";
		_serverContextMenu.AddItem("📝 Edit Configuration", 0);
		_serverContextMenu.AddItem("📂 Browse Files", 1);
		_serverContextMenu.AddSeparator();
		_serverContextMenu.AddItem("📋 Clone Server", 2);
		_serverContextMenu.AddSeparator();
		_serverContextMenu.AddItem("🗑️ Delete Server", 3);
		AddChild(_serverContextMenu);

		// Clone dialog
		_cloneDialog = new Window();
		_cloneDialog.Title = "Clone Server";
		_cloneDialog.Size = new Vector2I(400, 150);
		_cloneDialog.Visible = false;
		_cloneDialog.Exclusive = true;
		var cloneVBox = new VBoxContainer();
		cloneVBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		cloneVBox.AddThemeConstantOverride("separation", 10);
		var cloneMargin = new MarginContainer();
		cloneMargin.AddThemeConstantOverride("margin_left", 20);
		cloneMargin.AddThemeConstantOverride("margin_top", 20);
		cloneMargin.AddThemeConstantOverride("margin_right", 20);
		cloneMargin.AddThemeConstantOverride("margin_bottom", 20);
		cloneMargin.AddChild(cloneVBox);
		var cloneLabel = new Label { Text = "Enter a name for the cloned server:" };
		cloneVBox.AddChild(cloneLabel);
		_cloneNameInput = new LineEdit { PlaceholderText = "New Server Name" };
		cloneVBox.AddChild(_cloneNameInput);
		var cloneButtons = new HBoxContainer();
		cloneButtons.Alignment = BoxContainer.AlignmentMode.End;
		var cloneCancelBtn = new Button { Text = "Cancel" };
		cloneCancelBtn.Pressed += () => _cloneDialog.Hide();
		var cloneConfirmBtn = new Button { Text = "Clone" };
		cloneConfirmBtn.Pressed += OnCloneConfirmed;
		cloneButtons.AddChild(cloneCancelBtn);
		cloneButtons.AddChild(cloneConfirmBtn);
		cloneVBox.AddChild(cloneButtons);
		_cloneDialog.AddChild(cloneMargin);
		AddChild(_cloneDialog);

		// Delete confirmation dialog
		_deleteConfirmDialog = new ConfirmationDialog();
		_deleteConfirmDialog.Title = "Delete Server";
		_deleteConfirmDialog.Size = new Vector2I(450, 250);
		var deleteVBox = new VBoxContainer();
		deleteVBox.AddChild(new Label { Text = "Are you sure you want to delete this server?\nType the name below to confirm:" });

		_deleteConfirmCopy = new LineEdit { Editable = false, ThemeTypeVariation = "LineEditReadOnly" };
		deleteVBox.AddChild(new Label { Text = "Server Name (copy):", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		deleteVBox.AddChild(_deleteConfirmCopy);

		_deleteConfirmInput = new LineEdit { PlaceholderText = "Type server name here..." };
		deleteVBox.AddChild(new Label { Text = "Confirm Name:", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		deleteVBox.AddChild(_deleteConfirmInput);

		_deleteConfirmDialog.AddChild(deleteVBox);
		_deleteConfirmDialog.Confirmed += OnDeleteConfirmed;
		AddChild(_deleteConfirmDialog);

		// Server config dialog (for editing path, JAR, Java, mods, affinity)
		CreateServerConfigDialog();


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


	private void OnAddExistingPressed()
	{
		_addExistingUI.PopupCentered();
	}


	private void OnSidebarServerSelected(long index)
	{
		string name = _serverList.GetItemText((int)index);
		// Find in dropdown and select it to trigger existing logic
		for (int i = 0; i < _profileSelector.ItemCount; i++)
		{
			if (_profileSelector.GetItemText(i) == name)
			{
				_profileSelector.Select(i);
				OnProfileSelected(i);
				break;
			}
		}
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
		sm.ModConflictDetected += OnModConflictDetected;

		_commandInput.TextSubmitted += OnCommandSubmitted;
		_networkButton.Pressed += OnNetworkButtonPressed;
		_configButton.Pressed += OnConfigButtonPressed;
		_modpackButton.Pressed += OnModpackButtonPressed;
		_flagsButton.Pressed += OnFlagsButtonPressed;
		// _analyzeButton removed - analysis handled in wizard now
		_saveProfileButton.Pressed += OnSaveProfilePressed;
		_newServerButton.Pressed += OnAddProfilePressed;
		_deleteProfileButton.Pressed += OnDeleteProfilePressed;
		_profileSelector.ItemSelected += OnProfileSelected;

		// Sidebar signals
		_addExistingButton.Pressed += OnAddExistingPressed;
		_serverList.ItemSelected += OnSidebarServerSelected;
		_serverList.ItemClicked += OnServerListItemClicked;
		_serverContextMenu.IdPressed += OnServerContextMenuIdPressed;


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

		_dependencyManager.DownloadProgress += OnDownloadProgress;
		_dependencyManager.DownloadFinished += OnDownloadFinished;
		_dependencyManager.DownloadFailed += OnDownloadFailed;

		_optimizeManualButton.Pressed += OnOptimizeManualPressed;
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
		if (OperatingSystem.IsWindows())
		{
			long smartMask = AffinityHelper.GetSmartMask();
			SetCoreGridFromMask(smartMask);
			OnLogReceived(_currentProfileName, "[System] Manual grid populated with optimized physical core mask.", false);
		}
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

	public void RefreshProfiles()
	{
		_profiles = ProfileManager.LoadProfiles();
		_profileSelector.Clear();
		_profileSelector.AddItem("New Profile...");

		_serverList.Clear();

		foreach (var p in _profiles.OrderByDescending(x => x.LastUsed))
		{
			_profileSelector.AddItem(p.Name);
			int idx = _serverList.AddItem(p.Name);

			// Check status
			ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
			if (sm.IsRunning(p.Name))
			{
				_serverList.SetItemCustomBgColor(idx, new Color(0.2f, 0.4f, 0.2f));
			}
		}
	}

	private void OnProfileSelected(long index)
	{
		if (index == 0)
		{
			ClearInputs();
			return; // New Profile (Manual)
		}

		string name = _profileSelector.GetItemText((int)index);
		_currentProfileName = name;
		var p = _profiles.FirstOrDefault(x => x.Name == name);
		if (p != null)
		{
			_currentPath = p.Path;
			_currentJar = p.Jar;
			_currentJava = p.JavaPath;
			_maxRamInput.Text = p.MaxRam;
			_minRamInput.Text = p.MinRam;
			_currentModsPath = p.ModsPath;
			_flagsEditor.SetServerPath(p.Path);
			_useSmartAffinity = p.UseSmartAffinity;
			_currentAffinityMask = p.AffinityMask;
			SetCoreGridFromMask(p.AffinityMask);

			OnLogReceived(_currentProfileName, $"[System] Loaded profile: {name}", false);
			_systemMonitor.SetTargetProfile(_currentProfileName);
		}
	}

	private void ClearInputs()
	{
		_currentProfileName = "";
		_currentPath = "";
		_currentJar = "";
		_currentJava = "";
		_maxRamInput.Text = "2G";
		_minRamInput.Text = "1G";
		_currentModsPath = "";
		_useSmartAffinity = true;
		_currentAffinityMask = -1;
		InitializeCoreGrid();
		OnLogReceived("System", "Ready for new manual profile.", false);
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
		if (string.IsNullOrEmpty(_currentPath)) return;

		string name = Path.GetFileName(_currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(name)) name = "My Server";

		var p = new ProfileManager.ServerProfile
		{
			Name = name,
			Path = _currentPath,
			Jar = _currentJar,
			MaxRam = _maxRamInput.Text,
			MinRam = _minRamInput.Text,
			JavaPath = _currentJava,
			ModsPath = _currentModsPath,
			JvmFlags = _flagsEditor.GetFormattedFlags(),
			UseSmartAffinity = _useSmartAffinity,
			AffinityMask = _currentAffinityMask != -1 ? _currentAffinityMask : GetManualAffinityMask()
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

	private void OnConfigButtonPressed()
	{
		if (string.IsNullOrEmpty(_currentPath))
		{
			// If no path, maybe they want to download dependencies?
			_dependencyDownloader.PopupCentered();
			return;
		}
		_configEditor.Open(_currentProfileName, _currentPath);
	}

	// OnAnalyzeButtonPressed removed - JAR analysis now happens in wizard

	private void UpdateUIState(bool running)
	{
		// Simplified - path/jar/java fields removed, only control remaining elements
		_setupPanel.Visible = true;
		_profileSelector.Disabled = running;
		_saveProfileButton.Disabled = running;
		_deleteProfileButton.Disabled = running;
		_newServerButton.Disabled = running;
	}

	private void OnStatusChanged(string profileName, string newStatus)
	{
		// Update sidebar indicator
		for (int i = 0; i < _serverList.ItemCount; i++)
		{
			if (_serverList.GetItemText(i) == profileName)
			{
				if (newStatus == "Running") _serverList.SetItemCustomBgColor(i, new Color(0.2f, 0.4f, 0.2f));
				else if (newStatus == "Stopped") _serverList.SetItemCustomBgColor(i, new Color(0, 0, 0, 0));
				break;
			}
		}

		if (profileName != _currentProfileName) return;

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
		_currentPath = path;
		_currentJar = jar;
		_maxRamInput.Text = maxRam;
		_minRamInput.Text = minRam;
		_currentJava = javaPath;
		OnLogReceived(_currentProfileName, "[System] Configuration synced from host.", false);
	}

	private void SyncSettingsToNetwork()
	{
		NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
		if (nm.Multiplayer.IsServer())
		{
			nm.SyncConfigurationToAll(_currentPath, _currentJar, _maxRamInput.Text, _minRamInput.Text, _currentJava, _flagsEditor.GetFormattedFlags());
		}
	}

	private void OnConfigPropertiesSaved()
	{
		if (GetNode<ServerManager>("/root/ServerManager").IsRunning(_currentProfileName)) _restartNotification.Visible = true;
	}

	private void OnFlagsSaved()
	{
		if (GetNode<ServerManager>("/root/ServerManager").IsRunning(_currentProfileName)) _restartNotification.Visible = true;
		SyncSettingsToNetwork();
	}

	private void OnLogReceived(string profileName, string message, bool isError)
	{
		if (profileName != _currentProfileName) return;

		string timestamp = $"[color=#888888][{DateTime.Now:HH:mm:ss}][/color] ";
		string color = isError ? "#ff5555" : "#ffffff";
		if (message.Contains("[INFO]")) message = message.Replace("[INFO]", "[color=#55ff55][INFO][/color]");
		else if (message.Contains("[WARN]")) message = message.Replace("[WARN]", "[color=#ffff55][WARN][/color]");
		else if (message.Contains("[ERROR]")) message = message.Replace("[ERROR]", "[color=#ff5555][ERROR][/color]");
		_consoleOutput.AppendText($"{timestamp}[color={color}]{message}[/color]\n");
	}

	private void OnPlayerJoined(string profileName, string playerName) { if (profileName == _currentProfileName) _playerList.AddItem(playerName); OnLogReceived(profileName, $"[System] Player {playerName} joined.", false); }
	private void OnPlayerLeft(string profileName, string playerName) { if (profileName == _currentProfileName) { for (int i = 0; i < _playerList.ItemCount; i++) if (_playerList.GetItemText(i) == playerName) { _playerList.RemoveItem(i); break; } } OnLogReceived(profileName, $"[System] Player {playerName} left.", false); }

	private void OnModConflictDetected(string profileName, string[] modNames, string[] filenames)
	{
		if (profileName != _currentProfileName) return;

		_modConflictDialog.Setup(profileName, _currentPath, modNames, filenames);
	}

	public void OnModConflictResolved(string profileName)
	{
		if (profileName != _currentProfileName) return;

		OnLogReceived(profileName, "[System] Mod conflict resolved. Restarting server...", false);
		// Stop and restart
		GetNode<ServerManager>("/root/ServerManager").StopServer(profileName);

		// Wait a second for cleanup then restart
		GetTree().CreateTimer(1.5f).Connect("timeout", Callable.From(() =>
		{
			_on_start_stop_button_pressed(); // This will trigger start as it's now stopped
		}));
	}

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
		if (string.IsNullOrEmpty(_currentProfileName)) return;
		NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
		if (nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer()) nm.Rpc(nameof(NetworkManager.SendRemoteCommand), _currentProfileName, command);
		else GetNode<ServerManager>("/root/ServerManager").SendCommand(_currentProfileName, command);
	}

	private void _on_start_stop_button_pressed()
	{
		ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
		NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
		bool isClient = nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer();

		// Use the label text to determine the perceived state if in client mode
		bool perceivedRunning = isClient ? (_statusLabel.Text.Contains("Running") || _statusLabel.Text.Contains("Starting")) : sm.IsRunning(_currentProfileName);

		if (perceivedRunning)
		{
			if (isClient) nm.Rpc(nameof(NetworkManager.RequestStopServer), _currentProfileName);
			else sm.StopServer(_currentProfileName);
		}
		else
		{
			if (isClient) nm.Rpc(nameof(NetworkManager.RequestStartServer), _currentProfileName, _currentPath, _currentJar, _maxRamInput.Text, _minRamInput.Text, _currentJava, _flagsEditor.GetFormattedFlags());
			else
			{
				sm.StartServer(_currentProfileName, _currentPath, _currentJar, _maxRamInput.Text, _minRamInput.Text, _currentJava, _flagsEditor.GetFormattedFlags(), _currentModsPath);
				SyncSettingsToNetwork();

				// Apply Affinity
				GetTree().CreateTimer(0.5f).Connect("timeout", Callable.From(() =>
				{
					if (OperatingSystem.IsWindows())
					{
						if (_useSmartAffinity) sm.SetSmartAffinity(_currentProfileName);
						else
						{
							long mask = _currentAffinityMask != -1 ? _currentAffinityMask : GetManualAffinityMask();
							if (mask != -1) sm.SetManualAffinity(_currentProfileName, mask);
						}
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


	private void OnFlagsButtonPressed() { if (string.IsNullOrEmpty(_currentPath)) return; _flagsEditor.Open(_currentPath); }
	private void OnModpackButtonPressed() { if (string.IsNullOrEmpty(_currentPath)) return; _modpackDownloader.Open(_currentPath); }

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
			}
		}
	}

	private void ResetUI()
	{
		_setupPanel.Visible = true;
		_profileSelector.Disabled = false;
		_restartNotification.Visible = false;

		ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
		if (!sm.IsRunning(_currentProfileName))
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
	private void OnAddProfilePressed()
	{
		_serverSetupUI.PopupCentered();
	}

	private void OnDownloadProgress(string item, float itemProgress, float totalProgress)
	{
		string message = item.Contains("Installing") ? $"[System] {item}: {itemProgress:P0}" : $"[System] Downloading {item}: {itemProgress:P0}";
		OnLogReceived(_currentProfileName, message, false);
	}

	private void OnDownloadFinished(string item, string path)
	{
		OnLogReceived(_currentProfileName, $"[System] Finished downloading {item}. Saved to: {path}", false);
		RefreshProfiles(); // Refresh to pick up new Java/Loader
	}

	private void OnDownloadFailed(string item, string error)
	{
		OnLogReceived(_currentProfileName, $"[System] Failed to download {item}: {error}", true);
	}
	private void OnDeleteProfilePressed()
	{
		if (string.IsNullOrEmpty(_currentProfileName)) return;
		ProfileManager.DeleteProfile(_currentProfileName);
		OnLogReceived(_currentProfileName, $"[System] Deleted profile: {_currentProfileName}", false);
		RefreshProfiles();
		_profileSelector.Select(0);
		OnProfileSelected(0);
	}

	// =====================================================
	// SERVER CONTEXT MENU HANDLERS
	// =====================================================

	private void OnServerListItemClicked(long index, Vector2 atPosition, long mouseButtonIndex)
	{
		if (mouseButtonIndex == (long)MouseButton.Right && index >= 0)
		{
			_rightClickedServerName = _serverList.GetItemText((int)index);
			_serverContextMenu.Position = (Vector2I)DisplayServer.MouseGetPosition();
			_serverContextMenu.Popup();
		}
	}

	private void OnServerContextMenuIdPressed(long id)
	{
		if (string.IsNullOrEmpty(_rightClickedServerName)) return;

		switch (id)
		{
			case 0: // Edit Configuration
				OpenServerConfigDialog(_rightClickedServerName);
				break;
			case 1: // Browse Files
				BrowseServerFiles(_rightClickedServerName);
				break;
			case 2: // Clone Server
				_cloneNameInput.Text = _rightClickedServerName + " (Copy)";
				_cloneDialog.PopupCentered();
				break;
			case 3: // Delete Server
				_deleteConfirmInput.Text = "";
				_deleteConfirmCopy.Text = _rightClickedServerName;
				_deleteConfirmInput.PlaceholderText = $"Type '{_rightClickedServerName}'...";
				_deleteConfirmDialog.PopupCentered();
				break;
		}
	}

	private void BrowseServerFiles(string serverName)
	{
		var profile = _profiles.FirstOrDefault(p => p.Name == serverName);
		if (profile == null || string.IsNullOrEmpty(profile.Path)) return;

		if (Directory.Exists(profile.Path))
		{
			OS.ShellOpen(profile.Path);
		}
		else
		{
			OnLogReceived(_currentProfileName, $"[System] Server path does not exist: {profile.Path}", true);
		}
	}

	private void OnCloneConfirmed()
	{
		string newName = _cloneNameInput.Text.Trim();
		if (string.IsNullOrEmpty(newName))
		{
			OnLogReceived(_currentProfileName, "[System] Clone cancelled - no name provided.", true);
			return;
		}

		// Check if name already exists
		if (_profiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
		{
			OnLogReceived(_currentProfileName, $"[System] A server named '{newName}' already exists.", true);
			return;
		}

		var original = _profiles.FirstOrDefault(p => p.Name == _rightClickedServerName);
		if (original == null) return;

		var cloned = new ProfileManager.ServerProfile
		{
			Name = newName,
			Path = original.Path,
			Jar = original.Jar,
			MaxRam = original.MaxRam,
			MinRam = original.MinRam,
			JavaPath = original.JavaPath,
			JvmFlags = original.JvmFlags,
			ModsPath = original.ModsPath,
			AffinityMask = original.AffinityMask,
			UseSmartAffinity = original.UseSmartAffinity,
			LastUsed = DateTime.Now
		};

		ProfileManager.SaveProfile(cloned);
		RefreshProfiles();
		_cloneDialog.Hide();
		OnLogReceived(_currentProfileName, $"[System] Cloned '{_rightClickedServerName}' as '{newName}'", false);
	}

	private void OnDeleteConfirmed()
	{
		string typedName = _deleteConfirmInput.Text.Trim();
		if (!typedName.Equals(_rightClickedServerName, StringComparison.OrdinalIgnoreCase))
		{
			OnLogReceived(_currentProfileName, "[System] Deletion cancelled - server name did not match.", true);
			return;
		}

		ProfileManager.DeleteProfile(_rightClickedServerName);
		OnLogReceived(_currentProfileName, $"[System] Deleted server: {_rightClickedServerName}", false);
		RefreshProfiles();

		// If we deleted the currently selected server, reset to "New Profile"
		if (_currentProfileName == _rightClickedServerName)
		{
			_profileSelector.Select(0);
			OnProfileSelected(0);
		}
	}

	// =====================================================
	// SERVER CONFIG DIALOG
	// =====================================================

	// Config dialog fields
	private LineEdit _configPathInput;
	private LineEdit _configJarInput;
	private LineEdit _configJavaInput;
	private LineEdit _configModsInput;
	private CheckBox _configSmartAffinityCheck;
	private Button _configAffinityButton;
	private Label _configTestResultLabel;
	private Button _configInstallJavaButton;
	private int _configRequiredJavaVersion;
	private string _configEditingServer;

	private void CreateServerConfigDialog()
	{
		_serverConfigDialog = new Window();
		_serverConfigDialog.Title = "Server Configuration";
		_serverConfigDialog.Size = new Vector2I(500, 650);
		_serverConfigDialog.Visible = false;
		_serverConfigDialog.Exclusive = true;

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);

		// Server Path
		vbox.AddChild(new Label { Text = "Server Path:" });
		var pathBox = new HBoxContainer();
		_configPathInput = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var browsePathBtn = new Button { Text = "..." };
		browsePathBtn.Pressed += () =>
		{
			var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem };
			fd.DirSelected += (path) => { _configPathInput.Text = path; fd.QueueFree(); };
			_serverConfigDialog.AddChild(fd);
			fd.PopupCentered(new Vector2I(600, 400));
		};
		pathBox.AddChild(_configPathInput);
		pathBox.AddChild(browsePathBtn);
		vbox.AddChild(pathBox);

		// JAR File
		vbox.AddChild(new Label { Text = "Server JAR:" });
		var jarBox = new HBoxContainer();
		_configJarInput = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var browseJarBtn = new Button { Text = "..." };
		browseJarBtn.Pressed += () =>
		{
			var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem };
			fd.Filters = new string[] { "*.jar ; JAR Files" };
			fd.FileSelected += (path) => { _configJarInput.Text = Path.GetFileName(path); fd.QueueFree(); };
			_serverConfigDialog.AddChild(fd);
			fd.PopupCentered(new Vector2I(600, 400));
		};
		jarBox.AddChild(_configJarInput);
		jarBox.AddChild(browseJarBtn);
		vbox.AddChild(jarBox);

		// Java Path
		vbox.AddChild(new Label { Text = "Java Executable:" });
		var javaBox = new HBoxContainer();
		_configJavaInput = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = "Leave blank for auto-detect" };
		var browseJavaBtn = new Button { Text = "..." };
		browseJavaBtn.Pressed += () =>
		{
			var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem };
			fd.Filters = new string[] { "java.exe ; Java Executable", "* ; All Files" };
			fd.FileSelected += (path) => { _configJavaInput.Text = path; fd.QueueFree(); };
			_serverConfigDialog.AddChild(fd);
			fd.PopupCentered(new Vector2I(600, 400));
		};
		javaBox.AddChild(_configJavaInput);
		javaBox.AddChild(browseJavaBtn);
		vbox.AddChild(javaBox);

		// Mods Path - clarify with Fabric/Quilt note
		vbox.AddChild(new Label { Text = "Custom Mods Folder (Fabric/Quilt only):" });
		var modsBox = new HBoxContainer();
		_configModsInput = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = "Leave blank for default ./mods" };
		var browseModsBtn = new Button { Text = "..." };
		browseModsBtn.Pressed += () =>
		{
			var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem };
			fd.DirSelected += (path) => { _configModsInput.Text = path; fd.QueueFree(); };
			_serverConfigDialog.AddChild(fd);
			fd.PopupCentered(new Vector2I(600, 400));
		};
		modsBox.AddChild(_configModsInput);
		modsBox.AddChild(browseModsBtn);
		vbox.AddChild(modsBox);
		var modsNote = new Label
		{
			Text = "💡 Tip: Use CurseForge to create a modpack, then point this to its mods folder.\n     Forge/NeoForge users: place mods directly in your server's ./mods folder.",
			Modulate = new Color(0.6f, 0.6f, 0.6f),
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		vbox.AddChild(modsNote);

		// CPU Affinity
		vbox.AddChild(new HSeparator());
		_configSmartAffinityCheck = new CheckBox { Text = "Use Smart CPU Affinity (recommended)", ButtonPressed = true };
		vbox.AddChild(_configSmartAffinityCheck);
		_configAffinityButton = new Button { Text = "Configure Manual Affinity..." };
		_configAffinityButton.Pressed += ShowAffinityPicker;
		vbox.AddChild(_configAffinityButton);

		// Test Paths Section
		vbox.AddChild(new HSeparator());
		_configTestResultLabel = new Label { Text = "", CustomMinimumSize = new Vector2(0, 50) };
		_configTestResultLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		var testBtn = new Button { Text = "🔍 Test Paths" };
		testBtn.Pressed += OnTestPathsPressed;
		vbox.AddChild(testBtn);
		vbox.AddChild(_configTestResultLabel);

		// Install Java button (initially hidden)
		_configInstallJavaButton = new Button { Text = "📥 Install Java", Visible = false };
		_configInstallJavaButton.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1.0f));
		_configInstallJavaButton.Pressed += OnInstallJavaPressed;
		vbox.AddChild(_configInstallJavaButton);

		// Buttons
		var buttonBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttonBox.AddThemeConstantOverride("separation", 10);
		var cancelBtn = new Button { Text = "Cancel" };
		cancelBtn.Pressed += () => _serverConfigDialog.Hide();
		var saveBtn = new Button { Text = "Save" };
		saveBtn.Pressed += OnServerConfigSave;
		buttonBox.AddChild(cancelBtn);
		buttonBox.AddChild(saveBtn);
		vbox.AddChild(buttonBox);

		margin.AddChild(vbox);
		_serverConfigDialog.AddChild(margin);
		AddChild(_serverConfigDialog);
	}

	private void OpenServerConfigDialog(string serverName)
	{
		var profile = _profiles.FirstOrDefault(p => p.Name == serverName);
		if (profile == null) return;

		_configEditingServer = serverName;
		_configPathInput.Text = profile.Path ?? "";
		_configJarInput.Text = profile.Jar ?? "";
		_configJavaInput.Text = profile.JavaPath ?? "";
		_configModsInput.Text = profile.ModsPath ?? "";
		_configSmartAffinityCheck.ButtonPressed = profile.UseSmartAffinity;

		// Load affinity mask into grid if we want manual
		SetCoreGridFromMask(profile.AffinityMask);

		_serverConfigDialog.Title = $"Server Configuration - {serverName}";
		_serverConfigDialog.PopupCentered();
	}

	private void OnServerConfigSave()
	{
		var profile = _profiles.FirstOrDefault(p => p.Name == _configEditingServer);
		if (profile == null) return;

		profile.Path = _configPathInput.Text;
		profile.Jar = _configJarInput.Text;
		profile.JavaPath = _configJavaInput.Text;
		profile.ModsPath = _configModsInput.Text;
		profile.UseSmartAffinity = _configSmartAffinityCheck.ButtonPressed;
		profile.AffinityMask = GetManualAffinityMask();

		ProfileManager.SaveProfile(profile);
		_serverConfigDialog.Hide();
		OnLogReceived(_currentProfileName, $"[System] Updated configuration for '{_configEditingServer}'", false);

		// If we just edited the currently selected profile, reload it
		if (_configEditingServer == _currentProfileName)
		{
			_currentPath = profile.Path;
			_currentJar = profile.Jar;
			_currentJava = profile.JavaPath;
			_currentModsPath = profile.ModsPath;
			_useSmartAffinity = profile.UseSmartAffinity;
			_currentAffinityMask = profile.AffinityMask;
		}
	}

	private void OnTestPathsPressed()
	{
		var results = new List<string>();
		bool allPassed = true;
		bool needsJavaInstall = false;
		_configRequiredJavaVersion = 0;

		string serverPath = _configPathInput.Text.Trim();
		string jarName = _configJarInput.Text.Trim();
		string javaPath = _configJavaInput.Text.Trim();
		string modsPath = _configModsInput.Text.Trim();

		// Test Server Path
		if (string.IsNullOrEmpty(serverPath))
		{
			results.Add("❌ Server path is empty");
			allPassed = false;
		}
		else if (!Directory.Exists(serverPath))
		{
			results.Add($"❌ Server path not found: {serverPath}");
			allPassed = false;
		}
		else
		{
			results.Add("✅ Server path exists");
		}

		// Test JAR File and detect Java requirement
		int requiredJava = 0;
		if (string.IsNullOrEmpty(jarName))
		{
			results.Add("❌ JAR filename is empty");
			allPassed = false;
		}
		else if (!string.IsNullOrEmpty(serverPath))
		{
			string fullJarPath = Path.Combine(serverPath, jarName);
			bool isForgeMarker = jarName == "FORGE_INSTALLED" || jarName == "NEOFORGE_INSTALLED";

			if (!isForgeMarker && !File.Exists(fullJarPath))
			{
				results.Add($"❌ JAR not found: {jarName}");
				allPassed = false;
			}
			else
			{
				results.Add(isForgeMarker ? $"✅ Managed {jarName.Replace("_INSTALLED", "")} detection" : $"✅ JAR file found: {jarName}");

				// Detect Java version requirement
				if (isForgeMarker)
				{
					// For modern Forge, we usually need Java 17 or 21
					// We'll try to guess based on version later, but for now let's assume 17+
					requiredJava = 17;
				}
				else
				{
					requiredJava = JavaHelper.DetectRequiredJava(fullJarPath);
				}

				if (requiredJava > 0)
				{
					results.Add($"   └─ Requires Java {requiredJava}");
					_configRequiredJavaVersion = requiredJava;
				}
			}
		}

		// Test Java Path - now with smart availability checking
		bool usingAutoDetect = string.IsNullOrEmpty(javaPath) || javaPath.ToLower() == "auto" || javaPath == "Default (java)" || javaPath == "java";

		if (usingAutoDetect && requiredJava > 0)
		{
			// Check if we can find the required Java version
			string bestJava = JavaHelper.GetBestJavaPath(requiredJava);
			if (bestJava == "java")
			{
				// No suitable Java found - check if required version is specifically missing
				var availableJavas = JavaHelper.ScanForJava();
				var hasRequiredVersion = availableJavas.Any(j => j.MajorVersion >= requiredJava);

				if (!hasRequiredVersion)
				{
					results.Add($"❌ Java {requiredJava} not found on system");
					results.Add($"   └─ Click 'Install Java {requiredJava}' below to download");
					allPassed = false;
					needsJavaInstall = true;
				}
				else
				{
					results.Add($"✅ Java {requiredJava}+ available (auto-detect)");
				}
			}
			else
			{
				results.Add($"✅ Java found: {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(bestJava)))}");
			}
		}
		else if (usingAutoDetect)
		{
			results.Add("ℹ️ Java: Using auto-detect");
		}
		else if (!File.Exists(javaPath) && !Directory.Exists(javaPath))
		{
			results.Add($"❌ Java not found: {javaPath}");
			allPassed = false;
			if (requiredJava > 0)
			{
				needsJavaInstall = true;
			}
		}
		else
		{
			string actualExe = javaPath;
			if (Directory.Exists(javaPath))
			{
				actualExe = JavaHelper.FindJavaExeInDir(javaPath);
			}

			if (string.IsNullOrEmpty(actualExe) || !File.Exists(actualExe))
			{
				results.Add($"❌ Java executable not found in: {javaPath}");
				allPassed = false;
			}
			else
			{
				results.Add($"✅ Java executable found");
			}
		}

		// Test Mods Path (optional)
		if (!string.IsNullOrEmpty(modsPath))
		{
			if (!Directory.Exists(modsPath))
			{
				results.Add($"❌ Mods folder not found: {modsPath}");
				allPassed = false;
			}
			else
			{
				int modCount = Directory.GetFiles(modsPath, "*.jar").Length;
				results.Add($"✅ Mods folder found ({modCount} .jar files)");
			}
		}

		// Set result label
		_configTestResultLabel.Text = string.Join("\n", results);
		if (allPassed)
		{
			_configTestResultLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1.0f, 0.4f)); // Green
		}
		else
		{
			_configTestResultLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f)); // Red
		}

		// Show/hide the Install Java button
		if (needsJavaInstall && _configRequiredJavaVersion > 0)
		{
			_configInstallJavaButton.Text = $"📥 Install Java {_configRequiredJavaVersion}";
			_configInstallJavaButton.Visible = true;
		}
		else
		{
			_configInstallJavaButton.Visible = false;
		}
	}

	private async void OnInstallJavaPressed()
	{
		if (_configRequiredJavaVersion <= 0) return;

		_configInstallJavaButton.Disabled = true;
		_configInstallJavaButton.Text = $"⏳ Downloading Java {_configRequiredJavaVersion}...";

		try
		{
			string javaExe = await _dependencyManager.DownloadJDK(_configRequiredJavaVersion);

			if (!string.IsNullOrEmpty(javaExe))
			{
				_configTestResultLabel.Text = $"✅ Java {_configRequiredJavaVersion} installed successfully!\n   └─ Path: {javaExe}";
				_configTestResultLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1.0f, 0.4f));
				_configInstallJavaButton.Visible = false;

				// Auto-run test again to update status
				OnTestPathsPressed();
			}
			else
			{
				_configTestResultLabel.Text = $"❌ Failed to install Java {_configRequiredJavaVersion}";
				_configTestResultLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
			}
		}
		catch (Exception ex)
		{
			_configTestResultLabel.Text = $"❌ Error installing Java: {ex.Message}";
			_configTestResultLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
		}
		finally
		{
			_configInstallJavaButton.Disabled = false;
			_configInstallJavaButton.Text = $"📥 Install Java {_configRequiredJavaVersion}";
		}
	}
}
