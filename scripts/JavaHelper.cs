using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

public static class JavaHelper
{
    private static readonly string[] CommonJavaPaths = {
        Path.Combine(OS.GetUserDataDir(), "jdk"),
        @"C:\Program Files\Java",
        @"C:\Program Files\Eclipse Adoptium",
        @"C:\Program Files\BellSoft",
        @"C:\Program Files\Zulu",
        @"C:\Program Files\Microsoft",
        @"C:\Program Files (x86)\Java",
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Programs", "Adoptium"),
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Programs", "Java")
    };

    public struct JavaInfo
    {
        public int MajorVersion;
        public string Path;
    }

    public static List<JavaInfo> ScanForJava()
    {
        var found = new List<JavaInfo>();
        GD.Print("[JavaHelper] Starting system-wide Java scan...");

        // 1. Scan Common Folders
        foreach (var basePath in CommonJavaPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(basePath))
                {
                    string javaExe = FindJavaExeInDir(dir);
                    if (!string.IsNullOrEmpty(javaExe))
                    {
                        int version = GetMajorVersion(dir);
                        if (version > 0 && !found.Any(j => j.Path.ToLower() == javaExe.ToLower()))
                        {
                            found.Add(new JavaInfo { MajorVersion = version, Path = javaExe });
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Scan Registry
        ScanRegistry(found, @"SOFTWARE\JavaSoft\JDK", "JavaHome");
        ScanRegistry(found, @"SOFTWARE\JavaSoft\Java Runtime Environment", "JavaHome");
        ScanRegistry(found, @"SOFTWARE\Eclipse Foundation\JDK", "Path");
        ScanRegistry(found, @"SOFTWARE\AdoptOpenJDK\JDK", "Path");

        // 3. Scan PATH
        ScanPathEnv(found);

        // 4. Try 'where java'
        RunWhereJava(found);

        return found.OrderByDescending(j => j.MajorVersion).ToList();
    }

    private static void RunWhereJava(List<JavaInfo> found)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "where",
                Arguments = "java",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (File.Exists(line) && !found.Any(j => j.Path.ToLower() == line.ToLower()))
                    {
                        int version = GetMajorVersion(line);
                        if (version == 0) version = GetMajorVersion(Path.GetDirectoryName(line));
                        if (version > 0)
                        {
                            GD.Print($"[JavaHelper] Found Java {version} via 'where java': {line}");
                            found.Add(new JavaInfo { MajorVersion = version, Path = line });
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void ScanPathEnv(List<JavaInfo> found)
    {
        try
        {
            var pathEnv = System.Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return;
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    string javaExe = Path.Combine(p, "java.exe");
                    if (File.Exists(javaExe))
                    {
                        int version = GetMajorVersion(p);
                        if (version > 0 && !found.Any(j => j.Path.ToLower() == javaExe.ToLower()))
                        {
                            found.Add(new JavaInfo { MajorVersion = version, Path = javaExe });
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanRegistry(List<JavaInfo> found, string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using (var baseKey = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (baseKey == null) return;
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    using (var subKey = baseKey.OpenSubKey(subKeyName))
                    {
                        string path = subKey?.GetValue(valueName)?.ToString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            string javaExe = Path.Combine(path, "bin", "java.exe");
                            if (!File.Exists(javaExe)) javaExe = Path.Combine(path, "java.exe");

                            if (File.Exists(javaExe))
                            {
                                int version = GetMajorVersion(subKeyName);
                                if (version == 0) version = GetMajorVersion(path);
                                if (version > 0 && !found.Any(j => j.Path.ToLower() == javaExe.ToLower()))
                                {
                                    found.Add(new JavaInfo { MajorVersion = version, Path = javaExe });
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    public static string FindJavaExeInDir(string dir)
    {
        string binPath = Path.Combine(dir, "bin", "java.exe");
        if (File.Exists(binPath)) return binPath;
        string directPath = Path.Combine(dir, "java.exe");
        if (File.Exists(directPath)) return directPath;
        return null;
    }

    public static int DetectRequiredJava(string jarPath)
    {
        if (!File.Exists(jarPath)) return 0;
        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(jarPath))
            {
                // 1. Check fabric.mod.json
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using (StreamReader reader = new StreamReader(fabricEntry.Open()))
                    {
                        var json = JsonDocument.Parse(reader.ReadToEnd());
                        if (json.RootElement.TryGetProperty("depends", out var depends))
                        {
                            if (depends.TryGetProperty("java", out var javaProp))
                            {
                                string v = javaProp.GetString();
                                var match = Regex.Match(v, @"([0-9]+)");
                                if (match.Success) return int.Parse(match.Groups[1].Value);
                            }

                            if (depends.TryGetProperty("minecraft", out var mcProp))
                            {
                                string v = mcProp.GetString();
                                var match = Regex.Match(v, @"([0-9]+\.[0-9]+(?:\.[0-9]+)?)");
                                if (match.Success) return GetJavaForMCVersion(match.Groups[1].Value);
                            }
                        }
                    }
                }

                // 2. Check version.json (Vanilla/Legacy)
                var versionEntry = archive.GetEntry("version.json");
                if (versionEntry != null)
                {
                    using (StreamReader reader = new StreamReader(versionEntry.Open()))
                    {
                        var json = JsonDocument.Parse(reader.ReadToEnd());
                        if (json.RootElement.TryGetProperty("id", out var idProp))
                        {
                            return GetJavaForMCVersion(idProp.GetString());
                        }
                    }
                }

                // 3. Check for specific entry names indicating MC version (common in older jars)
                var mcVersionEntry = archive.Entries.FirstOrDefault(e => e.Name.StartsWith("minecraft_version_"));
                if (mcVersionEntry != null)
                {
                    var match = Regex.Match(mcVersionEntry.Name, @"minecraft_version_([0-9]+\.[0-9]+(?:\.[0-9]+)?)");
                    if (match.Success) return GetJavaForMCVersion(match.Groups[1].Value);
                }
            }
        }
        catch { }

        var filenameMatch = Regex.Match(Path.GetFileName(jarPath), "([0-9]+\\.[0-9]+(?:\\.[0-9]+)?)");
        if (filenameMatch.Success) return GetJavaForMCVersion(filenameMatch.Groups[1].Value);

        return 0;
    }

    public static int GetJavaForMCVersion(string mcVersion)
    {
        if (string.IsNullOrEmpty(mcVersion)) return 8;

        // Clean up version string (e.g. >=1.20.1 -> 1.20.1)
        var cleanMatch = Regex.Match(mcVersion, @"([0-9]+\.[0-9]+(?:\.[0-9]+)?)");
        if (cleanMatch.Success) mcVersion = cleanMatch.Groups[1].Value;

        var parts = mcVersion.Split('.');
        if (parts.Length >= 2)
        {
            if (int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
            {
                if (major > 1) return 21;
                if (major == 1)
                {
                    if (minor >= 21) return 21;
                    if (minor >= 20.5) return 21; // 1.20.5+ requires Java 21
                    if (minor == 20)
                    {
                        if (parts.Length >= 3 && int.TryParse(parts[2], out int patch) && patch >= 5) return 21;
                        return 17;
                    }
                    if (minor >= 18) return 17;
                    if (minor >= 17) return 16;
                    if (minor >= 13) return 8; // Actually 8 is fine for 1.13-1.16
                    if (minor >= 7) return 8;
                }
            }
        }
        return 8;
    }

    private static int GetMajorVersion(string text)
    {
        var lower = text.ToLower();
        var match = Regex.Match(lower, @"(?:jdk-|jre-|jdk|jre|version-|java-)([0-9]+)");
        if (match.Success)
        {
            int v = int.Parse(match.Groups[1].Value);
            if (v == 1)
            {
                var subMatch = Regex.Match(lower, @"1\.([0-9]+)");
                if (subMatch.Success) return int.Parse(subMatch.Groups[1].Value);
            }
            return v;
        }
        var numbers = Regex.Matches(text, @"[0-9]+");
        foreach (Match n in numbers)
        {
            int v = int.Parse(n.Value);
            if (v >= 17 && v <= 30) return v;
        }
        foreach (Match n in numbers)
        {
            int v = int.Parse(n.Value);
            if (v >= 8 && v <= 16) return v;
        }
        return 0;
    }

    public static string GetBestJavaPath(int requiredVersion)
    {
        var allJavas = ScanForJava();
        var exact = allJavas.FirstOrDefault(j => j.MajorVersion == requiredVersion);
        if (!string.IsNullOrEmpty(exact.Path)) return exact.Path;
        var higher = allJavas.Where(j => j.MajorVersion > requiredVersion).OrderBy(j => j.MajorVersion).FirstOrDefault();
        if (!string.IsNullOrEmpty(higher.Path)) return higher.Path;
        return "java";
    }
}
