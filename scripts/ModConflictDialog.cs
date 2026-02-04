using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class ModConflictDialog : Window
{
    private VBoxContainer _modContainer;
    private CheckBox _selectAllCheck;
    private string _currentProfile;
    private string _currentPath;

    public override void _Ready()
    {
        Title = "Mod Compatibility Conflict";
        Size = new Vector2I(420, 280);
        Exclusive = true;
        Transient = true;
        CloseRequested += Hide;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        var warningLabel = new Label();
        warningLabel.Text = "These client-only mods crashed the server:";
        warningLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(warningLabel);

        // Scroll container with fixed height
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(0, 80);
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        _modContainer = new VBoxContainer();
        _modContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_modContainer);

        _selectAllCheck = new CheckBox();
        _selectAllCheck.Text = "Select All";
        _selectAllCheck.ButtonPressed = true;
        _selectAllCheck.Toggled += (pressed) =>
        {
            foreach (var child in _modContainer.GetChildren())
            {
                if (child is CheckBox cb) cb.ButtonPressed = pressed;
            }
        };
        vbox.AddChild(_selectAllCheck);

        var note = new Label();
        note.Text = "Also remove from CurseForge to make permanent!";
        note.Modulate = new Color(1f, 1f, 0.6f);
        vbox.AddChild(note);

        // Button row
        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 10);
        buttonRow.Alignment = BoxContainer.AlignmentMode.End;
        vbox.AddChild(buttonRow);

        var ignoreBtn = new Button();
        ignoreBtn.Text = "Ignore";
        ignoreBtn.Pressed += () => Hide();
        buttonRow.AddChild(ignoreBtn);

        var removeBtn = new Button();
        removeBtn.Text = "Remove & Restart";
        removeBtn.Pressed += OnConfirmed;
        buttonRow.AddChild(removeBtn);
    }

    public void Setup(string profileName, string serverPath, string[] modNames, string[] filenames)
    {
        _currentProfile = profileName;
        _currentPath = serverPath;

        foreach (Node child in _modContainer.GetChildren()) child.QueueFree();

        for (int i = 0; i < modNames.Length; i++)
        {
            string name = modNames[i];
            string file = (filenames.Length > i) ? filenames[i] : "";

            var cb = new CheckBox();
            cb.Text = string.IsNullOrEmpty(file) ? name : $"{name} ({file})";
            cb.ButtonPressed = true;
            cb.SetMeta("filename", file);
            _modContainer.AddChild(cb);
        }

        PopupCentered();
    }

    private void OnConfirmed()
    {
        int count = 0;
        foreach (var child in _modContainer.GetChildren())
        {
            if (child is CheckBox cb && cb.ButtonPressed)
            {
                string filename = cb.GetMeta("filename").ToString();
                if (!string.IsNullOrEmpty(filename))
                {
                    ModSyncHelper.RemoveMod(Path.Combine(_currentPath, "mods"), filename);
                    count++;
                }
            }
        }

        Hide();

        if (count > 0)
        {
            GD.Print($"[ModSync] Removed {count} conflicting mods. Requesting restart...");
            var mainScreen = GetTree().Root.GetNodeOrNull<MainScreen>("MainScreen");
            if (mainScreen == null)
            {
                foreach (var child in GetTree().Root.GetChildren())
                {
                    if (child is MainScreen ms) { mainScreen = ms; break; }
                }
            }
            mainScreen?.OnModConflictResolved(_currentProfile);
        }
    }
}
