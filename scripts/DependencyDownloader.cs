using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class DependencyDownloader : Window
{
    private VBoxContainer _listContainer;
    private DependencyManager _dm;

    public void Setup(DependencyManager dm)
    {
        _dm = dm;
    }

    public override void _Ready()
    {
        Title = "Dependency Downloader";
        Size = new Vector2I(500, 400);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        panel.AddChild(margin);

        var vbox = new VBoxContainer();
        margin.AddChild(vbox);

        var label = new Label { Text = "Download Required Dependencies", ThemeTypeVariation = "HeaderLarge" };
        vbox.AddChild(label);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(scroll);

        _listContainer = new VBoxContainer();
        scroll.AddChild(_listContainer);

        var closeButton = new Button { Text = "Close", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        closeButton.Pressed += Hide;
        vbox.AddChild(closeButton);

        VisibilityChanged += () => { if (Visible) RefreshList(); };
    }

    public void RefreshList()
    {
        foreach (var child in _listContainer.GetChildren()) child.QueueFree();

        AddDependencyRow("Java 21 (Latest MC)", () => _dm.DownloadJDK(21));
        AddDependencyRow("Java 17 (1.17 - 1.20)", () => _dm.DownloadJDK(17));
        AddDependencyRow("Java 8 (Legacy)", () => _dm.DownloadJDK(8));

        // Add some spacing
        _listContainer.AddChild(new HSeparator());

        // Future: Fabric/Paper version selectors could go here
        _listContainer.AddChild(new Label { Text = "Note: Loaders (Fabric/Paper) are usually downloaded during Server Setup.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void AddDependencyRow(string name, Func<Task> downloadAction)
    {
        var hbox = new HBoxContainer();
        _listContainer.AddChild(hbox);

        var label = new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hbox.AddChild(label);

        var btn = new Button { Text = "Download" };
        btn.Pressed += async () =>
        {
            btn.Disabled = true;
            btn.Text = "Downloading...";
            await downloadAction();
            btn.Text = "Downloaded";
            // btn stays disabled or changes back
        };
        hbox.AddChild(btn);
    }
}
