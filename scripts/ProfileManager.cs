using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

public static class ProfileManager
{
    private static readonly string RegistryPath = Path.Combine(OS.GetUserDataDir(), "registry.json");
    private static readonly string LegacyConfigPath = Path.Combine(OS.GetUserDataDir(), "servers.json");
    private const string LocalProfileName = ".ez_profile.json";

    public class ServerProfile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Jar { get; set; }
        public string MaxRam { get; set; }
        public string MinRam { get; set; }
        public string JavaPath { get; set; }
        public string JvmFlags { get; set; }
        public string ModsPath { get; set; }
        public long AffinityMask { get; set; } = -1;
        public bool UseSmartAffinity { get; set; } = true;
        public DateTime LastUsed { get; set; }
    }

    private class Registry
    {
        public List<string> ServerPaths { get; set; } = new List<string>();
    }

    public static List<ServerProfile> LoadProfiles()
    {
        // Try migration first
        if (File.Exists(LegacyConfigPath))
        {
            PerformMigration();
        }

        if (!File.Exists(RegistryPath)) return new List<ServerProfile>();

        try
        {
            string json = File.ReadAllText(RegistryPath);
            var registry = JsonSerializer.Deserialize<Registry>(json);
            if (registry == null) return new List<ServerProfile>();

            var profiles = new List<ServerProfile>();
            var validPaths = new List<string>();

            foreach (var path in registry.ServerPaths)
            {
                if (!Directory.Exists(path)) continue;

                string profilePath = Path.Combine(path, LocalProfileName);
                if (File.Exists(profilePath))
                {
                    try
                    {
                        string profJson = File.ReadAllText(profilePath);
                        var p = JsonSerializer.Deserialize<ServerProfile>(profJson);
                        if (p != null)
                        {
                            p.Path = path; // Ensure path is correct
                            profiles.Add(p);
                            validPaths.Add(path);
                        }
                    }
                    catch { }
                }
            }

            // Clean up registry if paths vanished
            if (validPaths.Count != registry.ServerPaths.Count)
            {
                registry.ServerPaths = validPaths;
                SaveRegistry(registry);
            }

            return profiles.OrderByDescending(p => p.LastUsed).ToList();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to load profiles: {e.Message}");
            return new List<ServerProfile>();
        }
    }

    public static void SaveProfile(ServerProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Path) || !Directory.Exists(profile.Path)) return;

        profile.LastUsed = DateTime.Now;

        try
        {
            // Save local profile
            string profJson = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(profile.Path, LocalProfileName), profJson);

            // Update registry
            var registry = LoadRegistry();
            if (!registry.ServerPaths.Any(p => p.TrimEnd(Path.DirectorySeparatorChar) == profile.Path.TrimEnd(Path.DirectorySeparatorChar)))
            {
                registry.ServerPaths.Add(profile.Path);
                SaveRegistry(registry);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to save profile {profile.Name}: {e.Message}");
        }
    }

    public static void DeleteProfile(string name)
    {
        var profiles = LoadProfiles();
        var target = profiles.FirstOrDefault(p => p.Name == name);
        if (target == null) return;

        var registry = LoadRegistry();
        registry.ServerPaths.RemoveAll(p => p.TrimEnd(Path.DirectorySeparatorChar) == target.Path.TrimEnd(Path.DirectorySeparatorChar));
        SaveRegistry(registry);

        // Optional: rename the local profile to .ez_profile.json.bak
        string localProf = Path.Combine(target.Path, LocalProfileName);
        if (File.Exists(localProf))
        {
            try { File.Move(localProf, localProf + ".bak", true); } catch { }
        }
    }

    private static Registry LoadRegistry()
    {
        if (!File.Exists(RegistryPath)) return new Registry();
        try
        {
            string json = File.ReadAllText(RegistryPath);
            return JsonSerializer.Deserialize<Registry>(json) ?? new Registry();
        }
        catch { return new Registry(); }
    }

    private static void SaveRegistry(Registry reg)
    {
        try
        {
            string json = JsonSerializer.Serialize(reg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RegistryPath, json);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to save registry: {e.Message}");
        }
    }

    private static void PerformMigration()
    {
        GD.Print("[ProfileManager] Starting migration from servers.json to registry.json...");
        try
        {
            string json = File.ReadAllText(LegacyConfigPath);
            var legacyProfiles = JsonSerializer.Deserialize<List<ServerProfile>>(json);
            if (legacyProfiles == null) return;

            var registry = new Registry();
            foreach (var p in legacyProfiles)
            {
                if (Directory.Exists(p.Path))
                {
                    // Save local profile
                    string profJson = JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(p.Path, LocalProfileName), profJson);
                    registry.ServerPaths.Add(p.Path);
                }
            }

            SaveRegistry(registry);
            File.Move(LegacyConfigPath, LegacyConfigPath + ".migrated", true);
            GD.Print("[ProfileManager] Migration complete.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Migration failed: {e.Message}");
        }
    }
}
