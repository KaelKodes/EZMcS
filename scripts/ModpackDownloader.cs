using Godot;
using System;

public partial class ModpackDownloader : Window
{
    private LineEdit _urlInput;
    private Button _downloadButton;
    private ProgressBar _progressBar;
    private Label _statusLabel;
    private string _serverPath;

    public override void _Ready()
    {
        _urlInput = GetNode<LineEdit>("%UrlInput");
        _downloadButton = GetNode<Button>("%DownloadButton");
        _progressBar = GetNode<ProgressBar>("%ProgressBar");
        _statusLabel = GetNode<Label>("%StatusLabel");

        _downloadButton.Pressed += OnDownloadPressed;

        var helper = GetNode<ModpackHelper>("/root/ModpackHelper");
        helper.DownloadProgress += (percent) => _progressBar.Value = percent * 100;
        helper.DownloadFinished += OnDownloadFinished;
        helper.DownloadError += OnDownloadError;

        CloseRequested += Hide;
    }

    public void Open(string serverPath)
    {
        _serverPath = serverPath;
        _progressBar.Value = 0;
        _statusLabel.Text = "Idle";
        Show();
    }

    private void OnDownloadPressed()
    {
        string url = _urlInput.Text;
        if (string.IsNullOrEmpty(url)) return;

        _downloadButton.Disabled = true;
        _statusLabel.Text = "Downloading...";
        GetNode<ModpackHelper>("/root/ModpackHelper").DownloadModpack(url, _serverPath);
    }

    private void OnDownloadFinished(string path)
    {
        _downloadButton.Disabled = false;
        _statusLabel.Text = "Finished and Extracted!";
    }

    private void OnDownloadError(string message)
    {
        _downloadButton.Disabled = false;
        _statusLabel.Text = "Error: " + message;
        GD.PrintErr("Download error: " + message);
    }
}
