using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class AddExistingUI : Window
{
    private LineEdit _pathInput;
    private LineEdit _nameInput;
    private OptionButton _jarDropdown;
    private Label _analysisLabel;
    private Button _addButton;
    private MainScreen _mainScreen;

    public void Setup(MainScreen mainScreen)
    {
        _mainScreen = mainScreen;
    }

    public override void _Ready()
    {
        Title = "Add Existing Server";
        Size = new Vector2I(500, 450);
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
        vbox.AddThemeConstantOverride("separation", 15);
        margin.AddChild(vbox);

        vbox.AddChild(new Label { Text = "Add Existing Server", ThemeTypeVariation = "HeaderLarge" });
        vbox.AddChild(new Label { Text = "Select the folder where your Minecraft server is located.", Modulate = new Color(0.7f, 0.7f, 0.7f) });

        // Path Selection
        vbox.AddChild(new Label { Text = "Server Folder:", Modulate = new Color(0.7f, 0.7f, 0.7f) });
        var pathBox = new HBoxContainer();
        _pathInput = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = "C:\\Games\\MyServer" };
        var browseBtn = new Button { Text = "📂 Browse" };
        browseBtn.Pressed += OnBrowsePressed;
        pathBox.AddChild(_pathInput);
        pathBox.AddChild(browseBtn);
        vbox.AddChild(pathBox);

        // Name
        vbox.AddChild(new Label { Text = "Profile Name:", Modulate = new Color(0.7f, 0.7f, 0.7f) });
        _nameInput = new LineEdit { PlaceholderText = "My Existing Server" };
        vbox.AddChild(_nameInput);

        // JAR Selection
        vbox.AddChild(new Label { Text = "Main Server JAR / Marker:", Modulate = new Color(0.7f, 0.7f, 0.7f) });
        _jarDropdown = new OptionButton { CustomMinimumSize = new Vector2(0, 35) };
        vbox.AddChild(_jarDropdown);

        // Analysis Results
        _analysisLabel = new Label
        {
            Text = "Select a folder to analyze...",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 60),
            Modulate = new Color(0.6f, 0.6f, 0.6f)
        };
        vbox.AddChild(_analysisLabel);

        vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Footer
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 10);
        var cancelBtn = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(90, 35) };
        cancelBtn.Pressed += Hide;
        _addButton = new Button { Text = "Add Server", CustomMinimumSize = new Vector2(110, 35), Disabled = true };
        _addButton.Pressed += OnAddPressed;
        footer.AddChild(cancelBtn);
        footer.AddChild(_addButton);
        vbox.AddChild(footer);

        _pathInput.TextChanged += (text) => AnalyzeFolder(text);
    }

    private void OnBrowsePressed()
    {
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem };
        fd.DirSelected += (path) =>
        {
            _pathInput.Text = path;
            AnalyzeFolder(path);
            fd.QueueFree();
        };
        AddChild(fd);
        fd.PopupCentered(new Vector2I(600, 400));
    }

    private async void AnalyzeFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _addButton.Disabled = true;
            _analysisLabel.Text = "Please select a valid directory.";
            return;
        }

        _analysisLabel.Text = "🔍 Analyzing folder...";
        _jarDropdown.Clear();

        List<string> options = new List<string>();

        // 1. Check for modern Forge/NeoForge markers
        if (File.Exists(Path.Combine(path, "libraries", "net", "minecraftforge", "forge")) || Directory.Exists(Path.Combine(path, "libraries", "net", "minecraftforge", "forge")))
        {
            options.Add("FORGE_INSTALLED");
        }
        if (File.Exists(Path.Combine(path, "libraries", "net", "neoforged", "neoforge")) || Directory.Exists(Path.Combine(path, "libraries", "net", "neoforged", "neoforge")))
        {
            options.Add("NEOFORGE_INSTALLED");
        }

        // 2. Scan for JARs
        string[] jars = Directory.GetFiles(path, "*.jar");
        foreach (var jar in jars)
        {
            string fileName = Path.GetFileName(jar);
            // Filter out common installers or non-server jars if possible, but keep most
            if (!fileName.ToLower().Contains("installer"))
            {
                options.Add(fileName);
            }
        }

        if (options.Count == 0)
        {
            _analysisLabel.Text = "⚠️ No server JARs or Forge markers found in this folder.";
            _analysisLabel.Modulate = new Color(1f, 0.6f, 0.6f);
            _addButton.Disabled = true;
            return;
        }

        foreach (var opt in options) _jarDropdown.AddItem(opt);

        // Default name if empty
        if (string.IsNullOrEmpty(_nameInput.Text))
        {
            _nameInput.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        }

        // Use ModpackAnalyzer to get more details if mods folder exists
        string modsPath = Path.Combine(path, "mods");
        var result = await Task.Run(() => Directory.Exists(modsPath) ? ModpackAnalyzer.AnalyzeModsFolder(modsPath) : null);

        if (result != null && result.ModCount > 0)
        {
            _analysisLabel.Text = $"✅ Detected {result.ModCount} mods.\n" +
                                 $"Loader: {result.DetectedLoader} | MC: {result.DetectedMcVersion}\n" +
                                 $"Requires Java {result.RequiredJavaVersion}";
            _analysisLabel.Modulate = new Color(0.4f, 1f, 0.4f);

            // Try to pre-select JAR based on loader
            for (int i = 0; i < _jarDropdown.ItemCount; i++)
            {
                string item = _jarDropdown.GetItemText(i);
                if (item.Contains(result.DetectedLoader, StringComparison.OrdinalIgnoreCase))
                {
                    _jarDropdown.Select(i);
                    break;
                }
            }
        }
        else
        {
            _analysisLabel.Text = "✅ Folder looks like a Minecraft server. Select your main JAR above.";
            _analysisLabel.Modulate = new Color(0.7f, 1.0f, 0.7f);
        }

        _addButton.Disabled = false;
    }

    private void OnAddPressed()
    {
        string name = _nameInput.Text.Trim();
        string path = _pathInput.Text.Trim();
        string jar = _jarDropdown.GetItemText(_jarDropdown.Selected);

        if (string.IsNullOrEmpty(name)) return;

        string modsPath = Path.Combine(path, "mods");
        if (!Directory.Exists(modsPath)) modsPath = "";

        var profile = new ProfileManager.ServerProfile
        {
            Name = name,
            Path = path,
            Jar = jar,
            MaxRam = "4G",
            MinRam = "2G",
            JavaPath = "auto",
            ModsPath = modsPath,
            LastUsed = DateTime.Now
        };

        ProfileManager.SaveProfile(profile);
        _mainScreen?.RefreshProfiles();
        Hide();
    }
}
