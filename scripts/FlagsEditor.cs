using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public partial class FlagsEditor : Window
{
    [Signal] public delegate void FlagsSavedEventHandler();

    private VBoxContainer _flagsList;
    private LineEdit _maxPacketInput;
    private LineEdit _customFlagsInput;
    private Button _saveButton;
    private Button _cancelButton;

    private string _serverPath;
    private Dictionary<string, CheckBox> _checkBoxes = new Dictionary<string, CheckBox>();

    private static readonly Dictionary<string, string> CommonFlags = new Dictionary<string, string>
    {
        { "Use G1GC", "-XX:+UseG1GC" },
        { "Parallel Ref Proc", "-XX:+ParallelRefProcEnabled" },
        { "Max GC Pause (200ms)", "-XX:MaxGCPauseMillis=200" },
        { "Unlock Experimental", "-XX:+UnlockExperimentalVMOptions" },
        { "Disable Explicit GC", "-XX:+DisableExplicitGC" },
        { "Always Pre-Touch", "-XX:+AlwaysPreTouch" },
        { "G1 Heap Waste (5%)", "-XX:G1HeapWastePercent=5" },
        { "G1 Mixed GC Count (4)", "-XX:G1MixedGCCountTarget=4" },
        { "Perf Disable Shared Mem", "-XX:+PerfDisableSharedMem" },
        { "Max Tenuring Threshold", "-XX:MaxTenuringThreshold=1" }
    };

    public override void _Ready()
    {
        _flagsList = GetNode<VBoxContainer>("%FlagsList");
        _maxPacketInput = GetNode<LineEdit>("%MaxPacketInput");
        _customFlagsInput = GetNode<LineEdit>("%CustomFlagsInput");
        _saveButton = GetNode<Button>("%SaveButton");
        _cancelButton = GetNode<Button>("%CancelButton");

        _saveButton.Pressed += OnSavePressed;
        _cancelButton.Pressed += () => Hide();
        CloseRequested += Hide;

        foreach (var kvp in CommonFlags)
        {
            var cb = new CheckBox();
            cb.Text = kvp.Key;
            cb.TooltipText = kvp.Value;
            _flagsList.AddChild(cb);
            _checkBoxes[kvp.Value] = cb;
        }
    }

    public void Open(string serverPath)
    {
        _serverPath = serverPath;
        LoadFlags();
        Show();
    }

    private void LoadFlags()
    {
        string filePath = Path.Combine(_serverPath, "ez_flags.json");
        if (File.Exists(filePath))
        {
            try
            {
                var data = JsonSerializer.Deserialize<FlagsData>(File.ReadAllText(filePath));
                _customFlagsInput.Text = data.CustomFlags;
                _maxPacketInput.Text = data.MaxPacketSize;

                foreach (var flag in data.SelectedFlags)
                {
                    if (_checkBoxes.ContainsKey(flag))
                    {
                        _checkBoxes[flag].ButtonPressed = true;
                    }
                }
            }
            catch { }
        }
    }

    private void OnSavePressed()
    {
        var data = new FlagsData
        {
            CustomFlags = _customFlagsInput.Text,
            MaxPacketSize = _maxPacketInput.Text,
            SelectedFlags = new List<string>()
        };

        foreach (var kvp in _checkBoxes)
        {
            if (kvp.Value.ButtonPressed)
            {
                data.SelectedFlags.Add(kvp.Key);
            }
        }

        string filePath = Path.Combine(_serverPath, "ez_flags.json");
        File.WriteAllText(filePath, JsonSerializer.Serialize(data));

        EmitSignal(SignalName.FlagsSaved);
        Hide();
    }

    public string GetFormattedFlags()
    {
        if (string.IsNullOrEmpty(_serverPath)) return "";

        string filePath = Path.Combine(_serverPath, "ez_flags.json");
        if (!File.Exists(filePath)) return "";

        try
        {
            var data = JsonSerializer.Deserialize<FlagsData>(File.ReadAllText(filePath));
            var parts = new List<string>(data.SelectedFlags);

            if (!string.IsNullOrWhiteSpace(data.MaxPacketSize))
            {
                parts.Add($"-Dcom.mojang.minecraft.server.network.maxPacketSize={data.MaxPacketSize}");
            }

            if (!string.IsNullOrWhiteSpace(data.CustomFlags))
            {
                parts.Add(data.CustomFlags);
            }

            return string.Join(" ", parts);
        }
        catch { return ""; }
    }

    private class FlagsData
    {
        public List<string> SelectedFlags { get; set; }
        public string MaxPacketSize { get; set; }
        public string CustomFlags { get; set; }
    }
}
