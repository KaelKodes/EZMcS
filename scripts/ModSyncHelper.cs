using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class ModSyncHelper
{
    private const string BlacklistFileName = "modsync_blacklist.txt";

    /// <summary>
    /// Gets the path to the blacklist file for a server's mods folder.
    /// </summary>
    private static string GetBlacklistPath(string serverModsPath)
    {
        // Store blacklist in the server's root (parent of mods folder)
        string serverRoot = Path.GetDirectoryName(serverModsPath);
        return Path.Combine(serverRoot, BlacklistFileName);
    }

    /// <summary>
    /// Loads the blacklist of mod filenames that should not be synced.
    /// </summary>
    private static HashSet<string> LoadBlacklist(string serverModsPath)
    {
        var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = GetBlacklistPath(serverModsPath);
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        blacklist.Add(line.Trim());
                }
            }
        }
        catch { }
        return blacklist;
    }

    /// <summary>
    /// Saves the blacklist to disk.
    /// </summary>
    private static void SaveBlacklist(string serverModsPath, HashSet<string> blacklist)
    {
        try
        {
            string path = GetBlacklistPath(serverModsPath);
            File.WriteAllLines(path, blacklist);
        }
        catch { }
    }

    /// <summary>
    /// Adds a mod filename to the blacklist so it won't be synced again.
    /// </summary>
    public static void AddToBlacklist(string serverModsPath, string fileName)
    {
        var blacklist = LoadBlacklist(serverModsPath);
        blacklist.Add(fileName);
        SaveBlacklist(serverModsPath, blacklist);
        GD.Print($"[ModSync] Added '{fileName}' to sync blacklist.");
    }

    /// <summary>
    /// Clears the blacklist for a server.
    /// </summary>
    public static void ClearBlacklist(string serverModsPath)
    {
        try
        {
            string path = GetBlacklistPath(serverModsPath);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    public static void SyncMods(string sourcePath, string targetPath, Action<string> logCallback)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath)) return;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        // Safety: Don't sync if source and target are the same (e.g. adding an existing folder as its own source)
        string fullSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullSource.Equals(fullTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            // Load blacklist of mods we should NOT sync
            var blacklist = LoadBlacklist(targetPath);

            var sourceFiles = Directory.GetFiles(sourcePath, "*.jar");
            var targetFiles = Directory.GetFiles(targetPath, "*.jar");

            var sourceBasenames = sourceFiles.Select(Path.GetFileName).ToHashSet();

            int added = 0;
            int removed = 0;
            int updated = 0;
            int skipped = 0;

            // 1. Remove mods in target that are not in source (unless blacklisted - those are intentionally removed)
            foreach (var targetFile in targetFiles)
            {
                string fileName = Path.GetFileName(targetFile);
                if (!sourceBasenames.Contains(fileName))
                {
                    try
                    {
                        File.Delete(targetFile);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"[ModSync] Warning: Failed to remove old mod {fileName}: {ex.Message}");
                    }
                }
            }

            // 2. Copy or Update mods from source to target (skip blacklisted)
            foreach (var sourceFile in sourceFiles)
            {
                string fileName = Path.GetFileName(sourceFile);

                // Skip if blacklisted (user removed this mod intentionally)
                if (blacklist.Contains(fileName))
                {
                    skipped++;
                    continue;
                }

                string destFile = Path.Combine(targetPath, fileName);
                bool needsCopy = false;

                if (!File.Exists(destFile))
                {
                    needsCopy = true;
                    added++;
                }
                else
                {
                    FileInfo srcInfo = new FileInfo(sourceFile);
                    FileInfo destInfo = new FileInfo(destFile);

                    if (srcInfo.Length != destInfo.Length || srcInfo.LastWriteTime > destInfo.LastWriteTime)
                    {
                        needsCopy = true;
                        updated++;
                    }
                }

                if (needsCopy)
                {
                    try
                    {
                        File.Copy(sourceFile, destFile, true);
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"[ModSync] Error: Failed to copy {fileName}: {ex.Message}");
                    }
                }
            }

            if (added > 0 || updated > 0 || removed > 0)
            {
                string msg = $"[ModSync] Successfully synced mods: {added} added, {updated} updated, {removed} removed.";
                if (skipped > 0) msg += $" ({skipped} blacklisted skipped)";
                logCallback?.Invoke(msg);
            }
            else
            {
                logCallback?.Invoke("[ModSync] Mods are already up to date.");
            }
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"[ModSync] Fatal error during sync: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a mod from the server and adds it to the blacklist.
    /// </summary>
    public static void RemoveMod(string targetPath, string fileName)
    {
        try
        {
            string fullPath = Path.Combine(targetPath, fileName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                // Add to blacklist so it won't be synced back
                AddToBlacklist(targetPath, fileName);
            }
        }
        catch { }
    }
}
