using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public partial class ServerSetupUI : Window
{
	private LineEdit _nameInput;
	private OptionButton _versionDropdown;
	private CheckBox _snapshotCheck;
	private OptionButton _loaderVersionDropdown;
	private HBoxContainer _loaderContainer;
	private ButtonGroup _loaderGroup;

	private Button _createButton;
	private ProgressBar _progressBar;
	private Label _statusLabel;

	private ServerSetupWizard _wizard;
	private DependencyManager _dm;
	private MainScreen _mainScreen;

	// Advanced Settings
	private LineEdit _minRamInput;
	private LineEdit _maxRamInput;
	private LineEdit _portInput;
	private CheckBox _smartAffinityCheck;
	private CheckBox _eulaCheck;
	private CheckBox _startOnFinishCheck;

	// Java Integration
	private Label _javaStatusLabel;
	private Button _javaInstallBtn;
	private int _requiredJavaVersion = 8;

	// Modpack import
	private Button _importModpackButton;
	private LineEdit _modsPathInput;
	private Label _modpackAnalysisLabel;
	private string _detectedLoader = "";
	private string _detectedModsPath = "";
	private string _detectedLoaderVersion = "";

	public void Setup(ServerSetupWizard wizard, DependencyManager dm, MainScreen mainScreen)
	{
		_wizard = wizard;
		_dm = dm;
		_mainScreen = mainScreen;

		_dm.DownloadProgress += (item, itemP, totalP) =>
		{
			if (Visible) _progressBar.Value = totalP * 100;
		};

		_dm.DownloadFinished += (item, path) => { if (item.StartsWith("Java")) UpdateJavaStatus(); };
	}

	private string[] _loaders = { "Vanilla", "Fabric", "Paper", "Forge", "Quilt", "NeoForge" };

	public override void _Ready()
	{
		Title = "Create Server Profile";
		Size = new Vector2I(550, 700);
		Transient = true;
		Exclusive = true;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		vbox.CustomMinimumSize = new Vector2(450, 0);
		margin.AddChild(vbox);

		// Header
		var header = new Label { Text = "Create Server Profile", ThemeTypeVariation = "HeaderLarge" };
		vbox.AddChild(header);

		// =========== IMPORT FROM MODPACK SECTION ===========
		vbox.AddChild(new HSeparator());
		var importHeader = new Label { Text = "📦 Import from Modpack (Optional)", Modulate = new Color(0.5f, 0.8f, 1.0f) };
		vbox.AddChild(importHeader);

		var modsPathBox = new HBoxContainer();
		modsPathBox.AddThemeConstantOverride("separation", 8);
		_modsPathInput = new LineEdit
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			PlaceholderText = "Paste or browse to a mods folder...",
			CustomMinimumSize = new Vector2(0, 35)
		};
		var browseModsBtn = new Button { Text = "📂" };
		browseModsBtn.TooltipText = "Browse for mods folder";
		browseModsBtn.Pressed += OnBrowseModsFolder;
		var analyzeBtn = new Button { Text = "🔍 Analyze" };
		analyzeBtn.Pressed += () =>
		{
			if (!string.IsNullOrEmpty(_modsPathInput.Text.Trim()))
			{
				_detectedModsPath = _modsPathInput.Text.Trim();
				AnalyzeModpackFolder(_detectedModsPath);
			}
		};
		modsPathBox.AddChild(_modsPathInput);
		modsPathBox.AddChild(browseModsBtn);
		modsPathBox.AddChild(analyzeBtn);
		vbox.AddChild(modsPathBox);

		_modpackAnalysisLabel = new Label
		{
			Text = "",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0, 40)
		};
		_modpackAnalysisLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
		vbox.AddChild(_modpackAnalysisLabel);

		// =========== MANUAL CONFIGURATION SECTION ===========
		vbox.AddChild(new HSeparator());
		vbox.AddChild(new Label { Text = "Server Configuration", Modulate = new Color(0.7f, 0.7f, 0.7f) });

		// Name
		vbox.AddChild(new Label { Text = "Profile Name", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		_nameInput = new LineEdit { PlaceholderText = "My Modded Server", CustomMinimumSize = new Vector2(0, 35) };
		vbox.AddChild(_nameInput);

		// Minecraft Version
		var versionLabelHbox = new HBoxContainer();
		vbox.AddChild(versionLabelHbox);
		versionLabelHbox.AddChild(new Label { Text = "Minecraft Version", Modulate = new Color(0.7f, 0.7f, 0.7f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		_snapshotCheck = new CheckBox { Text = "Snapshots", FocusMode = Control.FocusModeEnum.None };
		_snapshotCheck.Toggled += (on) => LoadInitialData(on);
		versionLabelHbox.AddChild(_snapshotCheck);

		_versionDropdown = new OptionButton { CustomMinimumSize = new Vector2(0, 35) };
		_versionDropdown.ItemSelected += (idx) => { UpdateLoaderVersions(); UpdateJavaStatus(); };
		vbox.AddChild(_versionDropdown);

		// Java Requirement Status
		var javaBox = new HBoxContainer();
		_javaStatusLabel = new Label { Text = "Checking Java...", Modulate = new Color(0.7f, 0.7f, 0.7f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_javaInstallBtn = new Button { Text = "Install Java", Visible = false };
		_javaInstallBtn.Pressed += OnInstallJavaPressed;
		javaBox.AddChild(_javaStatusLabel);
		javaBox.AddChild(_javaInstallBtn);
		vbox.AddChild(javaBox);

		// Modloader
		vbox.AddChild(new Label { Text = "Modloader", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		_loaderContainer = new HBoxContainer();
		_loaderContainer.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(_loaderContainer);

		_loaderGroup = new ButtonGroup();
		foreach (var loader in _loaders)
		{
			var cb = new CheckBox
			{
				Text = loader,
				ButtonGroup = _loaderGroup,
				ToggleMode = true,
				FocusMode = Control.FocusModeEnum.None
			};
			cb.Toggled += (on) => { if (on) UpdateLoaderVersions(); };
			_loaderContainer.AddChild(cb);
			if (loader == "Vanilla") cb.ButtonPressed = true;
		}

		// Modloader Version
		vbox.AddChild(new Label { Text = "Modloader Version", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		_loaderVersionDropdown = new OptionButton { CustomMinimumSize = new Vector2(0, 35) };
		vbox.AddChild(_loaderVersionDropdown);

		// =========== ADVANCED SETTINGS SECTION ===========
		vbox.AddChild(new HSeparator());
		var advancedHeader = new Label { Text = "⚙️ Advanced Settings", Modulate = new Color(0.7f, 1.0f, 0.7f) };
		vbox.AddChild(advancedHeader);

		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 20);
		grid.AddThemeConstantOverride("v_separation", 10);
		vbox.AddChild(grid);

		// RAM
		grid.AddChild(new Label { Text = "Memory (Min/Max):", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		var ramBox = new HBoxContainer();
		_minRamInput = new LineEdit { Text = "2G", CustomMinimumSize = new Vector2(80, 0) };
		ramBox.AddChild(_minRamInput);
		ramBox.AddChild(new Label { Text = "/" });
		_maxRamInput = new LineEdit { Text = "4G", CustomMinimumSize = new Vector2(80, 0) };
		ramBox.AddChild(_maxRamInput);
		grid.AddChild(ramBox);

		// Port
		grid.AddChild(new Label { Text = "Server Port:", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		_portInput = new LineEdit { Text = "25565", CustomMinimumSize = new Vector2(100, 0) };
		grid.AddChild(_portInput);

		// CPU Affinity
		grid.AddChild(new Control()); // Spacer
		_smartAffinityCheck = new CheckBox { Text = "Smart CPU Affinity", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
		grid.AddChild(_smartAffinityCheck);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		// EULA
		_eulaCheck = new CheckBox
		{
			Text = "I accept the Minecraft EULA",
			FocusMode = Control.FocusModeEnum.None,
			Modulate = new Color(1.0f, 0.8f, 0.5f)
		};
		vbox.AddChild(_eulaCheck);

		// Start on Finish
		_startOnFinishCheck = new CheckBox { Text = "Launch server immediately after creation", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
		vbox.AddChild(_startOnFinishCheck);

		vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

		// Progress/Status
		_statusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.7f, 0.7f, 0.7f) };
		vbox.AddChild(_statusLabel);
		_progressBar = new ProgressBar { Visible = false, CustomMinimumSize = new Vector2(0, 10) };
		vbox.AddChild(_progressBar);

		// Divider
		vbox.AddChild(new HSeparator());

		// Footer Buttons
		var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		footer.AddThemeConstantOverride("separation", 15);
		vbox.AddChild(footer);

		var cancelBtn = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(100, 40) };
		cancelBtn.Pressed += Hide;
		footer.AddChild(cancelBtn);

		_createButton = new Button { Text = "Create Server", CustomMinimumSize = new Vector2(120, 40) };
		_createButton.Pressed += OnCreatePressed;
		footer.AddChild(_createButton);

		VisibilityChanged += () => { if (Visible) LoadInitialData(_snapshotCheck.ButtonPressed); };
	}

	private void OnBrowseModsFolder()
	{
		var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem };
		fd.DirSelected += OnModsFolderSelected;
		AddChild(fd);
		fd.PopupCentered(new Vector2I(600, 400));
	}

	private void OnModsFolderSelected(string path)
	{
		_modsPathInput.Text = path;
		_detectedModsPath = path;
		AnalyzeModpackFolder(path);
	}

	private async void AnalyzeModpackFolder(string modsPath)
	{
		_modpackAnalysisLabel.Text = "🔍 Analyzing mods folder...";
		_modpackAnalysisLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.5f));

		ModpackAnalyzer.AnalysisResult result = null;
		await Task.Run(() => { result = ModpackAnalyzer.AnalyzeModsFolder(modsPath); });

		if (result == null || result.ModCount == 0)
		{
			_modpackAnalysisLabel.Text = "❌ No mods found in folder";
			_modpackAnalysisLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
			return;
		}

		// Build analysis summary
		var summary = new List<string>();
		summary.Add($"✅ Found {result.ModCount} mods");

		if (!string.IsNullOrEmpty(result.DetectedLoader) && result.DetectedLoader != "Unknown")
		{
			summary.Add($"   └─ Loader: {result.DetectedLoader}");
			_detectedLoader = result.DetectedLoader;
			SelectLoader(result.DetectedLoader);
		}

		// Store detected loader version to select after versions load
		if (!string.IsNullOrEmpty(result.DetectedLoaderVersion))
		{
			_detectedLoaderVersion = result.DetectedLoaderVersion;
			summary.Add($"   └─ Loader Version: {result.DetectedLoaderVersion}");
		}

		if (!string.IsNullOrEmpty(result.DetectedMcVersion))
		{
			summary.Add($"   └─ MC Version: {result.DetectedMcVersion}");
			await SelectMcVersion(result.DetectedMcVersion);
			// Now load loader versions and wait for it to complete before selecting
			await LoadLoaderVersionsAsync();
			// After loader versions are loaded, select the detected one
			SelectLoaderVersionImmediate(_detectedLoaderVersion);
		}

		if (result.RequiredJavaVersion > 0)
		{
			summary.Add($"   └─ Requires Java {result.RequiredJavaVersion}");
		}

		if (result.Warnings.Count > 0)
		{
			foreach (var warn in result.Warnings.Take(2))
			{
				summary.Add($"⚠️ {warn}");
			}
		}

		_modpackAnalysisLabel.Text = string.Join("\n", summary);
		_modpackAnalysisLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

		// Auto-populate name from folder
		if (string.IsNullOrEmpty(_nameInput.Text))
		{
			string folderName = Path.GetFileName(modsPath.TrimEnd(Path.DirectorySeparatorChar));
			if (folderName.ToLower() == "mods")
			{
				// Use parent folder name instead
				folderName = Path.GetFileName(Path.GetDirectoryName(modsPath));
			}
			_nameInput.Text = folderName + " Server";
		}

		UpdateJavaStatus();
	}

	private void UpdateJavaStatus()
	{
		if (_versionDropdown.Selected == -1) return;
		string version = _versionDropdown.GetItemText(_versionDropdown.Selected);

		_requiredJavaVersion = JavaHelper.GetJavaForMCVersion(version);

		var found = JavaHelper.ScanForJava();
		bool hasRequired = found.Any(j => j.MajorVersion == _requiredJavaVersion || j.MajorVersion > _requiredJavaVersion);

		if (hasRequired)
		{
			_javaStatusLabel.Text = $"✅ Java {_requiredJavaVersion}+ Found";
			_javaStatusLabel.Modulate = new Color(0.4f, 1f, 0.4f);
			_javaInstallBtn.Visible = false;
		}
		else
		{
			_javaStatusLabel.Text = $"❌ Java {_requiredJavaVersion} Missing";
			_javaStatusLabel.Modulate = new Color(1f, 0.4f, 0.4f);
			_javaInstallBtn.Text = $"Install Java {_requiredJavaVersion}";
			_javaInstallBtn.Visible = true;
		}
	}

	private async void OnInstallJavaPressed()
	{
		_javaInstallBtn.Disabled = true;
		_progressBar.Visible = true;
		await _dm.DownloadJDK(_requiredJavaVersion);
		_javaInstallBtn.Disabled = false;
		UpdateJavaStatus();
	}

	private void SelectLoader(string loader)
	{
		foreach (Node child in _loaderContainer.GetChildren())
		{
			if (child is CheckBox cb && cb.Text == loader)
			{
				cb.ButtonPressed = true;
				break;
			}
		}
	}

	private async Task SelectMcVersion(string version)
	{
		// Wait for dropdown to be populated
		if (_versionDropdown.ItemCount <= 1)
		{
			await Task.Delay(500);
		}

		// Try exact match
		for (int i = 0; i < _versionDropdown.ItemCount; i++)
		{
			if (_versionDropdown.GetItemText(i) == version)
			{
				_versionDropdown.Select(i);
				return;
			}
		}

		// Try partial match (e.g., "1.20" matches "1.20.1", "1.20.4", etc.)
		for (int i = 0; i < _versionDropdown.ItemCount; i++)
		{
			if (_versionDropdown.GetItemText(i).StartsWith(version))
			{
				_versionDropdown.Select(i);
				return;
			}
		}
	}

	/// <summary>
	/// Loads loader versions and waits for the async operation to complete.
	/// </summary>
	private async Task LoadLoaderVersionsAsync()
	{
		if (_wizard == null || _dm == null) return;
		if (_versionDropdown.Disabled || _versionDropdown.Selected == -1) return;

		string mcVersion = _versionDropdown.GetItemText(_versionDropdown.Selected);
		string loader = (_loaderGroup.GetPressedButton() as Button)?.Text ?? "Vanilla";

		_loaderVersionDropdown.Clear();
		_loaderVersionDropdown.Disabled = true;

		if (loader == "Vanilla")
		{
			_loaderVersionDropdown.AddItem("N/A");
		}
		else
		{
			_loaderVersionDropdown.AddItem("Loading...");
			List<string> versions = new List<string>();

			if (loader == "Fabric")
			{
				versions = await _dm.GetFabricLoaderVersions(mcVersion);
			}
			else if (loader == "Paper")
			{
				versions = await _dm.GetPaperBuilds(mcVersion);
			}
			else if (loader == "Forge")
			{
				versions = await _dm.GetForgeVersions(mcVersion);
			}
			else if (loader == "NeoForge")
			{
				versions = await _dm.GetNeoForgeVersions(mcVersion);
			}

			// Inject detected version if it matches the current loader and isn't in the list
			if (!string.IsNullOrEmpty(_detectedLoaderVersion) && loader == _detectedLoader && !versions.Contains(_detectedLoaderVersion))
			{
				versions.Insert(0, _detectedLoaderVersion);
			}

			_loaderVersionDropdown.Clear();
			if (versions.Count == 0)
			{
				_loaderVersionDropdown.AddItem(loader == "Vanilla" ? "N/A" : "Coming Soon");
			}
			else
			{
				foreach (var v in versions) _loaderVersionDropdown.AddItem(v);
				_loaderVersionDropdown.Disabled = false;
			}
		}
	}

	/// <summary>
	/// Immediately selects a loader version in the dropdown (no waiting).
	/// </summary>
	private void SelectLoaderVersionImmediate(string version)
	{
		if (string.IsNullOrEmpty(version)) return;

		// Try exact match first
		for (int i = 0; i < _loaderVersionDropdown.ItemCount; i++)
		{
			if (_loaderVersionDropdown.GetItemText(i) == version)
			{
				_loaderVersionDropdown.Select(i);
				GD.Print($"[ServerSetupUI] Selected loader version: {version}");
				return;
			}
		}

		// Try partial/contains match
		for (int i = 0; i < _loaderVersionDropdown.ItemCount; i++)
		{
			if (_loaderVersionDropdown.GetItemText(i).Contains(version))
			{
				_loaderVersionDropdown.Select(i);
				GD.Print($"[ServerSetupUI] Selected loader version (partial): {_loaderVersionDropdown.GetItemText(i)}");
				return;
			}
		}

		GD.Print($"[ServerSetupUI] Loader version {version} not found in available versions.");
	}

	private async void LoadInitialData(bool includeSnapshots)
	{
		_versionDropdown.Clear();
		_versionDropdown.AddItem("Loading...");
		_versionDropdown.Disabled = true;

		var versions = await _wizard.GetAvailableVersions(includeSnapshots);

		_versionDropdown.Clear();
		foreach (var v in versions) _versionDropdown.AddItem(v);
		_versionDropdown.Disabled = false;

		UpdateLoaderVersions();
	}

	private async void UpdateLoaderVersions()
	{
		await LoadLoaderVersionsAsync();
	}

	private async void OnCreatePressed()
	{
		string name = _nameInput.Text.Trim();
		if (string.IsNullOrEmpty(name))
		{
			_statusLabel.Text = "Please enter a profile name";
			return;
		}

		if (_versionDropdown.Selected == -1) return;

		if (!_eulaCheck.ButtonPressed)
		{
			_statusLabel.Text = "Please accept the Minecraft EULA";
			_eulaCheck.Modulate = new Color(1f, 0.4f, 0.4f);
			return;
		}

		// Default path to user dir/servers/name
		string path = Path.Combine(OS.GetUserDataDir(), "servers", name);
		string version = _versionDropdown.GetItemText(_versionDropdown.Selected);
		string loader = (_loaderGroup.GetPressedButton() as Button)?.Text ?? "Vanilla";
		string loaderVersion = _loaderVersionDropdown.GetItemText(_loaderVersionDropdown.Selected);

		_createButton.Disabled = true;
		_progressBar.Visible = true;
		_progressBar.Value = 0;
		_statusLabel.Text = "Creating server folder...";

		try
		{
			await Task.Run(() => _wizard.InitializeServerFolder(path));

			string jarPath = "";
			if (loader == "Vanilla")
			{
				_statusLabel.Text = "Downloading Vanilla...";
				jarPath = await _wizard.DownloadVanillaJar(version, path);
			}
			else if (loader == "Fabric")
			{
				_statusLabel.Text = "Downloading Fabric...";
				jarPath = await _dm.DownloadFabric(version, path, loaderVersion);
			}
			else if (loader == "Paper")
			{
				_statusLabel.Text = "Downloading Paper...";
				jarPath = await _dm.DownloadPaper(version, path, loaderVersion);
			}
			else if (loader == "Forge")
			{
				_statusLabel.Text = "Installing Forge...";
				jarPath = await _dm.DownloadForge(version, path, loaderVersion);
			}
			else if (loader == "NeoForge")
			{
				_statusLabel.Text = "Installing NeoForge...";
				jarPath = await _dm.DownloadNeoForge(version, path, loaderVersion);
			}

			if (string.IsNullOrEmpty(jarPath))
			{
				_statusLabel.Text = "Failed to download!";
				_createButton.Disabled = false;
				return;
			}

			var profile = new ProfileManager.ServerProfile
			{
				Name = name,
				Path = path,
				Jar = Path.GetFileName(jarPath),
				MaxRam = _maxRamInput.Text,
				MinRam = _minRamInput.Text,
				JavaPath = "auto", // Use auto-detect by default
				ModsPath = !string.IsNullOrEmpty(_detectedModsPath) ? _detectedModsPath : "",
				LastUsed = DateTime.Now
			};

			// Save EULA
			await Task.Run(() =>
			{
				File.WriteAllLines(Path.Combine(path, "eula.txt"), new string[] { "# EZMinecraftServer Automatic EULA Accept", "eula=true" });

				// Write server.properties for Port
				string propsPath = Path.Combine(path, "server.properties");
				if (!File.Exists(propsPath))
				{
					File.WriteAllText(propsPath, $"server-port={_portInput.Text}\nquery.port={_portInput.Text}");
				}
			});

			ProfileManager.SaveProfile(profile);

			_statusLabel.Text = "Success!";
			await Task.Delay(1000);

			_mainScreen?.RefreshProfiles();

			if (_startOnFinishCheck.ButtonPressed)
			{
				ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
				sm.StartServer(profile.Name, profile.Path, profile.Jar, profile.MaxRam, profile.MinRam, profile.JavaPath, "", profile.ModsPath);

				if (_smartAffinityCheck.ButtonPressed && OperatingSystem.IsWindows())
				{
					sm.SetSmartAffinity(profile.Name);
				}
			}

			Hide();
		}
		catch (Exception e)
		{
			_statusLabel.Text = $"Error: {e.Message}";
			_createButton.Disabled = false;
		}
	}
}
