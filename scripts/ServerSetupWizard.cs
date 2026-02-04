using Godot;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;

public partial class ServerSetupWizard : Node
{
    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

    public async Task<List<string>> GetAvailableVersions(bool includeSnapshots = false)
    {
        try
        {
            string manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
            string manifestJson = await _httpClient.GetStringAsync(manifestUrl);
            var manifest = JsonDocument.Parse(manifestJson);
            var versions = manifest.RootElement.GetProperty("versions");

            var list = new List<string>();
            foreach (var v in versions.EnumerateArray())
            {
                string type = v.GetProperty("type").GetString();
                if (type == "release" || (includeSnapshots && type == "snapshot"))
                {
                    list.Add(v.GetProperty("id").GetString());
                }
            }
            return list;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ServerSetupWizard] Failed to fetch versions: {e.Message}");
            return new List<string> { "1.21.1", "1.20.1", "1.19.4", "1.18.2", "1.16.5" }; // Fallback
        }
    }

    public async Task<string> DownloadVanillaJar(string version, string targetPath)
    {
        // For simplicity, we can use a known URL or a version manifest
        // Mojang version manifest: https://launchermeta.mojang.com/mc/game/version_manifest.json
        // For this implementation, I'll provide a simplified version that fetches latest or specified version details.

        try
        {
            string manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
            string manifestJson = await _httpClient.GetStringAsync(manifestUrl);
            var manifest = JsonDocument.Parse(manifestJson);

            var versions = manifest.RootElement.GetProperty("versions");
            string versionUrl = "";

            foreach (var v in versions.EnumerateArray())
            {
                if (v.GetProperty("id").GetString() == version)
                {
                    versionUrl = v.GetProperty("url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(versionUrl)) return null;

            string versionDataJson = await _httpClient.GetStringAsync(versionUrl);
            var versionData = JsonDocument.Parse(versionDataJson);
            string downloadUrl = versionData.RootElement.GetProperty("downloads").GetProperty("server").GetProperty("url").GetString();

            string jarPath = Path.Combine(targetPath, "server.jar");
            var response = await _httpClient.GetAsync(downloadUrl);
            using (var fs = new FileStream(jarPath, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
            }

            return jarPath;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ServerSetupWizard] Failed to download Vanilla JAR: {e.Message}");
            return null;
        }
    }

    public void InitializeServerFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        // Create a default eula.txt with eula=false
        string eulaPath = Path.Combine(path, "eula.txt");
        if (!File.Exists(eulaPath))
        {
            File.WriteAllLines(eulaPath, new string[] { "# Minecraft EULA", "eula=false" });
        }

        // Create a default server.properties if it doesn't exist
        string propsPath = Path.Combine(path, "server.properties");
        if (!File.Exists(propsPath))
        {
            var props = new Dictionary<string, string>
            {
                { "server-port", "25565" },
                { "online-mode", "true" },
                { "motd", "A Minecraft Server powered by EZMinecraftServer" }
            };
            ConfigManager.SaveProperties(path, props);
        }
    }
}
