using Godot;
using System;
using System.Collections.Generic;

public partial class ConfigEditor : Window
{
    [Signal] public delegate void PropertiesSavedEventHandler();

    private VBoxContainer _propContainer;
    private Button _saveButton;
    private Button _cancelButton;
    private Button _eulaButton;
    private string _serverPath;
    private string _currentProfileName;
    private Dictionary<string, LineEdit> _inputs = new Dictionary<string, LineEdit>();

    public override void _Ready()
    {
        _propContainer = GetNode<VBoxContainer>("%PropContainer");
        _saveButton = GetNode<Button>("%SaveButton");
        _cancelButton = GetNode<Button>("%CancelButton");
        _eulaButton = GetNode<Button>("%EulaButton");

        _saveButton.Pressed += OnSavePressed;
        _eulaButton.Pressed += OnEulaPressed;
        _cancelButton.Pressed += () => Hide();
        CloseRequested += Hide;
    }

    public void Open(string profileName, string serverPath)
    {
        _currentProfileName = profileName;
        _serverPath = serverPath;
        _inputs.Clear();
        _serverPath = serverPath;

        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        if (nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer())
        {
            // Client Mode: Request from Host
            nm.Rpc(nameof(NetworkManager.RequestRemoteProperties), serverPath);
            AddLoadingMessage();
        }
        else
        {
            // Local Mode
            var props = ConfigManager.LoadProperties(serverPath);
            foreach (var kvp in props)
            {
                AddPropertyInput(kvp.Key, kvp.Value);
            }
        }

        Show();
    }

    private void AddLoadingMessage()
    {
        Label label = new Label();
        label.Text = "Loading properties from host...";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        _propContainer.AddChild(label);
    }

    public void HandleRemoteProperties(Godot.Collections.Dictionary props)
    {
        foreach (Node child in _propContainer.GetChildren()) child.QueueFree();
        _inputs.Clear();

        foreach (var key in props.Keys)
        {
            AddPropertyInput(key.ToString(), props[key].ToString());
        }
    }

    private void AddPropertyInput(string key, string value)
    {
        HBoxContainer hbox = new HBoxContainer();
        Label label = new Label();
        label.Text = key;
        label.CustomMinimumSize = new Vector2(150, 0);

        LineEdit input = new LineEdit();
        input.Text = value;
        input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        hbox.AddChild(label);
        hbox.AddChild(input);
        _propContainer.AddChild(hbox);

        _inputs[key] = input;
    }

    private void OnSavePressed()
    {
        var newProps = new Dictionary<string, string>();
        foreach (var kvp in _inputs)
        {
            newProps[kvp.Key] = kvp.Value.Text;
        }

        NetworkManager nm = GetNode<NetworkManager>("/root/NetworkManager");
        if (nm.Multiplayer.MultiplayerPeer != null && !nm.Multiplayer.IsServer())
        {
            var gDict = new Godot.Collections.Dictionary();
            foreach (var kvp in newProps) gDict[kvp.Key] = kvp.Value;
            nm.Rpc(nameof(NetworkManager.SaveRemoteProperties), _serverPath, gDict);
        }
        else
        {
            ConfigManager.SaveProperties(_serverPath, newProps);
        }

        EmitSignal(SignalName.PropertiesSaved);
        Hide();
    }

    private void OnEulaPressed()
    {
        GetNode<ServerManager>("/root/ServerManager").AcceptEula(_currentProfileName);
        // Refresh properties in case eula.txt was added
        Open(_currentProfileName, _serverPath);
    }
}
