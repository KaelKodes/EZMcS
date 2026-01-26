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

    public void Open(string serverPath)
    {
        _serverPath = serverPath;
        foreach (Node child in _propContainer.GetChildren()) child.QueueFree();
        _inputs.Clear();

        var props = ConfigManager.LoadProperties(serverPath);
        foreach (var kvp in props)
        {
            AddPropertyInput(kvp.Key, kvp.Value);
        }

        Show();
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

        ConfigManager.SaveProperties(_serverPath, newProps);
        EmitSignal(SignalName.PropertiesSaved);
        Hide();
    }

    private void OnEulaPressed()
    {
        GetNode<ServerManager>("/root/ServerManager").AcceptEula();
        // Refresh properties in case eula.txt was added
        Open(_serverPath);
    }
}
