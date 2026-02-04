using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Analyzes a mods folder to detect MC version, modloader type, and requirements.
/// Works by reading metadata from mod JARs (fabric.mod.json, mods.toml, quilt.mod.json).
/// </summary>
public static class ModpackAnalyzer
{
    public class AnalysisResult
    {
        public string DetectedLoader { get; set; } = "Unknown";
        public string DetectedMcVersion { get; set; } = "";
        public string DetectedLoaderVersion { get; set; } = "";
        public int RequiredJavaVersion { get; set; } = 0;
        public int ModCount { get; set; } = 0;
        public List<string> Warnings { get; set; } = new List<string>();
        public List<ModInfo> Mods { get; set; } = new List<ModInfo>();
    }

    public class ModInfo
    {
        public string FileName { get; set; }
        public string ModId { get; set; }
        public string ModName { get; set; }
        public string Version { get; set; }
        public string McVersion { get; set; }
        public string Loader { get; set; }
    }

    /// <summary>
    /// Analyzes a modpack folder to determine modloader and version requirements.
    /// First tries manifest.json (CurseForge), then falls back to scanning mod JARs.
    /// </summary>
    public static AnalysisResult AnalyzeModsFolder(string path)
    {
        // Check if this is a CurseForge modpack root with manifest.json
        string manifestPath = Path.Combine(path, "manifest.json");
        if (File.Exists(manifestPath))
        {
            var result = ParseCurseForgeManifest(manifestPath);
            if (result != null && !string.IsNullOrEmpty(result.DetectedMcVersion))
            {
                return result;
            }
        }

        // Check parent folder for manifest.json (if user selected /mods subfolder)
        string parentManifest = Path.Combine(Path.GetDirectoryName(path) ?? "", "manifest.json");
        if (File.Exists(parentManifest))
        {
            var result = ParseCurseForgeManifest(parentManifest);
            if (result != null && !string.IsNullOrEmpty(result.DetectedMcVersion))
            {
                // Count mods in the mods folder
                if (Directory.Exists(path))
                {
                    result.ModCount = Directory.GetFiles(path, "*.jar").Length;
                }
                return result;
            }
        }

        // Fall back to scanning mod JARs
        return AnalyzeModJars(path);
    }

    /// <summary>
    /// Parses a CurseForge manifest.json file for exact version info.
    /// </summary>
    private static AnalysisResult ParseCurseForgeManifest(string manifestPath)
    {
        try
        {
            string json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new AnalysisResult();

            // Get modpack name
            if (root.TryGetProperty("name", out var nameProp))
            {
                // We don't store this but could use for profile naming
            }

            // Get minecraft version and modloaders
            if (root.TryGetProperty("minecraft", out var minecraft))
            {
                if (minecraft.TryGetProperty("version", out var versionProp))
                {
                    result.DetectedMcVersion = versionProp.GetString();
                }

                if (minecraft.TryGetProperty("modLoaders", out var modLoaders) && modLoaders.ValueKind == JsonValueKind.Array)
                {
                    foreach (var loader in modLoaders.EnumerateArray())
                    {
                        if (loader.TryGetProperty("primary", out var primary) && primary.GetBoolean())
                        {
                            if (loader.TryGetProperty("id", out var idProp))
                            {
                                string loaderId = idProp.GetString(); // e.g., "fabric-0.18.1", "forge-47.2.0"
                                ParseLoaderId(loaderId, result);
                            }
                            break;
                        }
                        // If no primary, just take first one
                        if (loader.TryGetProperty("id", out var firstId))
                        {
                            string loaderId = firstId.GetString();
                            ParseLoaderId(loaderId, result);
                            break;
                        }
                    }
                }
            }

            // Count files in manifest
            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                result.ModCount = files.GetArrayLength();
            }

            // Infer Java version
            if (!string.IsNullOrEmpty(result.DetectedMcVersion))
            {
                result.RequiredJavaVersion = InferJavaVersion(result.DetectedMcVersion);
            }

            return result;
        }
        catch (Exception ex)
        {
            var result = new AnalysisResult();
            result.Warnings.Add($"Failed to parse manifest.json: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a loader ID like "fabric-0.18.1" or "forge-47.2.0" into loader name and version.
    /// </summary>
    private static void ParseLoaderId(string loaderId, AnalysisResult result)
    {
        if (string.IsNullOrEmpty(loaderId)) return;

        if (loaderId.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
        {
            result.DetectedLoader = "Fabric";
            result.DetectedLoaderVersion = loaderId.Substring(7); // Remove "fabric-"
        }
        else if (loaderId.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
        {
            result.DetectedLoader = "Forge";
            result.DetectedLoaderVersion = loaderId.Substring(6);
        }
        else if (loaderId.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
        {
            result.DetectedLoader = "NeoForge";
            result.DetectedLoaderVersion = loaderId.Substring(9);
        }
        else if (loaderId.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
        {
            result.DetectedLoader = "Quilt";
            result.DetectedLoaderVersion = loaderId.Substring(6);
        }
        else
        {
            result.DetectedLoader = loaderId; // Unknown format, use as-is
        }
    }

    /// <summary>
    /// Analyzes all .jar files in a mods folder to determine modloader and version requirements.
    /// </summary>
    private static AnalysisResult AnalyzeModJars(string modsPath)
    {
        var result = new AnalysisResult();

        if (!Directory.Exists(modsPath))
        {
            result.Warnings.Add("Mods folder does not exist");
            return result;
        }

        var jarFiles = Directory.GetFiles(modsPath, "*.jar");
        result.ModCount = jarFiles.Length;

        if (jarFiles.Length == 0)
        {
            result.Warnings.Add("No .jar files found in mods folder");
            return result;
        }

        var detectedLoaders = new Dictionary<string, int>();
        var detectedMcVersions = new Dictionary<string, int>();


        foreach (var jarPath in jarFiles)
        {
            try
            {
                var modInfo = AnalyzeModJar(jarPath);
                if (modInfo != null)
                {
                    result.Mods.Add(modInfo);

                    // Count loader types
                    if (!string.IsNullOrEmpty(modInfo.Loader))
                    {
                        detectedLoaders.TryGetValue(modInfo.Loader, out int count);
                        detectedLoaders[modInfo.Loader] = count + 1;
                    }

                    // Count MC versions
                    if (!string.IsNullOrEmpty(modInfo.McVersion))
                    {
                        // Normalize version (e.g., ">=1.20" -> "1.20", "1.20.x" -> "1.20")
                        string normalizedVersion = NormalizeMcVersion(modInfo.McVersion);
                        if (!string.IsNullOrEmpty(normalizedVersion))
                        {
                            detectedMcVersions.TryGetValue(normalizedVersion, out int count);
                            detectedMcVersions[normalizedVersion] = count + 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to analyze {Path.GetFileName(jarPath)}: {ex.Message}");
            }
        }

        // Determine most common loader
        if (detectedLoaders.Count > 0)
        {
            result.DetectedLoader = detectedLoaders.OrderByDescending(x => x.Value).First().Key;
        }

        // Determine most common MC version
        if (detectedMcVersions.Count > 0)
        {
            result.DetectedMcVersion = detectedMcVersions.OrderByDescending(x => x.Value).First().Key;
        }

        // Infer Java version from MC version
        if (!string.IsNullOrEmpty(result.DetectedMcVersion))
        {
            result.RequiredJavaVersion = InferJavaVersion(result.DetectedMcVersion);
        }

        // Add warning if mixed loaders detected
        if (detectedLoaders.Count > 1)
        {
            result.Warnings.Add($"Mixed modloaders detected: {string.Join(", ", detectedLoaders.Keys)}");
        }

        return result;
    }

    /// <summary>
    /// Analyzes a single mod JAR file to extract metadata.
    /// </summary>
    private static ModInfo AnalyzeModJar(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);

        // Try Fabric first (fabric.mod.json)
        var fabricEntry = archive.GetEntry("fabric.mod.json");
        if (fabricEntry != null)
        {
            using var stream = fabricEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return ParseFabricMod(jarPath, json);
        }

        // Try Quilt (quilt.mod.json)
        var quiltEntry = archive.GetEntry("quilt.mod.json");
        if (quiltEntry != null)
        {
            using var stream = quiltEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return ParseQuiltMod(jarPath, json);
        }

        // Try Forge/NeoForge (META-INF/mods.toml)
        var forgeEntry = archive.GetEntry("META-INF/mods.toml");
        if (forgeEntry != null)
        {
            using var stream = forgeEntry.Open();
            using var reader = new StreamReader(stream);
            var toml = reader.ReadToEnd();
            return ParseForgeMod(jarPath, toml);
        }

        // Try legacy Forge (mcmod.info)
        var legacyForgeEntry = archive.GetEntry("mcmod.info");
        if (legacyForgeEntry != null)
        {
            using var stream = legacyForgeEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return ParseLegacyForgeMod(jarPath, json);
        }

        return null;
    }

    private static ModInfo ParseFabricMod(string jarPath, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new ModInfo
            {
                FileName = Path.GetFileName(jarPath),
                Loader = "Fabric"
            };

            if (root.TryGetProperty("id", out var idProp))
                info.ModId = idProp.GetString();

            if (root.TryGetProperty("name", out var nameProp))
                info.ModName = nameProp.GetString();

            if (root.TryGetProperty("version", out var versionProp))
                info.Version = versionProp.GetString();

            // MC version is in depends.minecraft
            if (root.TryGetProperty("depends", out var depends))
            {
                if (depends.TryGetProperty("minecraft", out var mcVersion))
                {
                    info.McVersion = mcVersion.GetString();
                }
            }

            return info;
        }
        catch { return null; }
    }

    private static ModInfo ParseQuiltMod(string jarPath, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new ModInfo
            {
                FileName = Path.GetFileName(jarPath),
                Loader = "Quilt"
            };

            if (root.TryGetProperty("quilt_loader", out var loader))
            {
                if (loader.TryGetProperty("id", out var idProp))
                    info.ModId = idProp.GetString();

                if (loader.TryGetProperty("version", out var versionProp))
                    info.Version = versionProp.GetString();

                if (loader.TryGetProperty("metadata", out var metadata))
                {
                    if (metadata.TryGetProperty("name", out var nameProp))
                        info.ModName = nameProp.GetString();
                }

                // MC version in depends
                if (loader.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in depends.EnumerateArray())
                    {
                        if (dep.TryGetProperty("id", out var depId) && depId.GetString() == "minecraft")
                        {
                            if (dep.TryGetProperty("versions", out var versions))
                            {
                                info.McVersion = versions.GetString();
                            }
                        }
                    }
                }
            }

            return info;
        }
        catch { return null; }
    }

    private static ModInfo ParseForgeMod(string jarPath, string toml)
    {
        var info = new ModInfo
        {
            FileName = Path.GetFileName(jarPath),
            Loader = "Forge"
        };

        // Simple TOML parsing for key fields
        foreach (var line in toml.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("modId"))
            {
                info.ModId = ExtractTomlValue(trimmed);
            }
            else if (trimmed.StartsWith("displayName"))
            {
                info.ModName = ExtractTomlValue(trimmed);
            }
            else if (trimmed.StartsWith("version") && !trimmed.StartsWith("versionRange"))
            {
                info.Version = ExtractTomlValue(trimmed);
            }
            else if (trimmed.StartsWith("loaderVersion"))
            {
                // Check if it's NeoForge
                var val = ExtractTomlValue(trimmed);
                if (val != null && val.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                {
                    info.Loader = "NeoForge";
                }
            }
        }

        // Try to find MC version from dependencies section
        if (toml.Contains("modId=\"minecraft\"") || toml.Contains("modId = \"minecraft\""))
        {
            // Extract version range
            var mcMatch = System.Text.RegularExpressions.Regex.Match(toml, @"modId\s*=\s*""minecraft""[^]]*versionRange\s*=\s*""([^""]+)""");
            if (mcMatch.Success)
            {
                info.McVersion = mcMatch.Groups[1].Value;
            }
        }

        // Check for NeoForge-specific markers
        if (toml.Contains("neoforge") || toml.Contains("NeoForge"))
        {
            info.Loader = "NeoForge";
        }

        return info;
    }

    private static ModInfo ParseLegacyForgeMod(string jarPath, string json)
    {
        try
        {
            // mcmod.info can be an array or object
            var trimmed = json.Trim();
            if (trimmed.StartsWith("["))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    return ExtractLegacyForgeInfo(jarPath, first);
                }
            }
            else
            {
                using var doc = JsonDocument.Parse(json);
                return ExtractLegacyForgeInfo(jarPath, doc.RootElement);
            }
        }
        catch { }
        return null;
    }

    private static ModInfo ExtractLegacyForgeInfo(string jarPath, JsonElement element)
    {
        var info = new ModInfo
        {
            FileName = Path.GetFileName(jarPath),
            Loader = "Forge"
        };

        if (element.TryGetProperty("modid", out var idProp))
            info.ModId = idProp.GetString();

        if (element.TryGetProperty("name", out var nameProp))
            info.ModName = nameProp.GetString();

        if (element.TryGetProperty("version", out var versionProp))
            info.Version = versionProp.GetString();

        if (element.TryGetProperty("mcversion", out var mcProp))
            info.McVersion = mcProp.GetString();

        return info;
    }

    private static string ExtractTomlValue(string line)
    {
        var eqIdx = line.IndexOf('=');
        if (eqIdx < 0) return null;
        var value = line.Substring(eqIdx + 1).Trim().Trim('"', '\'');
        return value;
    }

    /// <summary>
    /// Normalizes MC version strings like ">=1.20.1", "1.20.x", "[1.20,1.21)" to base version.
    /// Prioritizes full 3-part versions (1.21.1) over shortened versions (1.21).
    /// </summary>
    private static string NormalizeMcVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return null;

        // First, try to extract a full 3-part version (1.21.1) using regex
        var fullVersionMatch = System.Text.RegularExpressions.Regex.Match(version, @"(\d+\.\d+\.\d+)");
        if (fullVersionMatch.Success)
        {
            return fullVersionMatch.Groups[1].Value;
        }

        // Remove common prefixes/suffixes
        version = version.Replace(">=", "").Replace("<=", "").Replace(">", "").Replace("<", "")
                         .Replace("~", "").Replace("^", "").Replace("*", "")
                         .Trim('[', ']', '(', ')', ' ');

        // Handle ranges like "1.20,1.21" - take first
        if (version.Contains(','))
        {
            version = version.Split(',')[0].Trim();
        }

        // Handle .x wildcards (e.g., "1.21.x" -> "1.21")
        version = version.Replace(".x", "");

        // Validate it looks like a version (at least major.minor)
        if (System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+"))
        {
            return version;
        }

        return null;
    }

    /// <summary>
    /// Infers Java version required from MC version.
    /// </summary>
    private static int InferJavaVersion(string mcVersion)
    {
        // Parse major.minor
        var parts = mcVersion.Split('.');
        if (parts.Length < 2) return 8;

        int major = 1;
        int minor = 0;
        int.TryParse(parts[0], out major);
        int.TryParse(parts[1], out minor);

        // MC 1.21+ -> Java 21
        if (major >= 1 && minor >= 21) return 21;
        // MC 1.18+ -> Java 17
        if (major >= 1 && minor >= 18) return 17;
        // MC 1.17 -> Java 16
        if (major >= 1 && minor >= 17) return 16;
        // MC 1.12+ -> Java 8
        return 8;
    }
}
