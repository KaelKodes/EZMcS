using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

public static class ProfileManager
{
    private static readonly string ConfigPath = Path.Combine(OS.GetUserDataDir(), "servers.json");

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

    public static List<ServerProfile> LoadProfiles()
    {
        if (!File.Exists(ConfigPath)) return new List<ServerProfile>();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<List<ServerProfile>>(json) ?? new List<ServerProfile>();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to load profiles: {e.Message}");
            return new List<ServerProfile>();
        }
    }

    public static void SaveProfile(ServerProfile profile)
    {
        var profiles = LoadProfiles();
        var existing = profiles.FirstOrDefault(p => p.Name == profile.Name);

        if (existing != null)
        {
            existing.Path = profile.Path;
            existing.Jar = profile.Jar;
            existing.MaxRam = profile.MaxRam;
            existing.MinRam = profile.MinRam;
            existing.JavaPath = profile.JavaPath;
            existing.JvmFlags = profile.JvmFlags;
            existing.ModsPath = profile.ModsPath;
            existing.AffinityMask = profile.AffinityMask;
            existing.UseSmartAffinity = profile.UseSmartAffinity;
            existing.LastUsed = DateTime.Now;
        }
        else
        {
            profile.LastUsed = DateTime.Now;
            profiles.Add(profile);
        }

        SaveAll(profiles);
    }

    public static void DeleteProfile(string name)
    {
        var profiles = LoadProfiles();
        profiles.RemoveAll(p => p.Name == name);
        SaveAll(profiles);
    }

    private static void SaveAll(List<ServerProfile> profiles)
    {
        try
        {
            string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to save profiles: {e.Message}");
        }
    }
}
