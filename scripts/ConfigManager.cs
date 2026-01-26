using Godot;
using System;
using System.Collections.Generic;
using System.IO;

public class ConfigManager
{
    public static Dictionary<string, string> LoadProperties(string path)
    {
        var properties = new Dictionary<string, string>();
        string filePath = Path.Combine(path, "server.properties");

        if (!File.Exists(filePath)) return properties;

        foreach (string line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex != -1)
            {
                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                properties[key] = value;
            }
        }

        return properties;
    }

    public static void SaveProperties(string path, Dictionary<string, string> properties)
    {
        string filePath = Path.Combine(path, "server.properties");
        List<string> lines = new List<string>();
        lines.Add("# Minecraft server properties");
        lines.Add("# Edited by EZMinecraftServer");

        foreach (var kvp in properties)
        {
            lines.Add($"{kvp.Key}={kvp.Value}");
        }

        File.WriteAllLines(filePath, lines);
    }
}
