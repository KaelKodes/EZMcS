using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

public partial class DependencyManager : Node
{
    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
    private static readonly string JavaStoragePath = Path.Combine(OS.GetUserDataDir(), "jdk");

    [Signal] public delegate void DownloadProgressEventHandler(string item, float itemProgress, float totalProgress);
    [Signal] public delegate void DownloadFinishedEventHandler(string item, string path);
    [Signal] public delegate void DownloadFailedEventHandler(string item, string error);

    public override void _Ready()
    {
        if (!Directory.Exists(JavaStoragePath)) Directory.CreateDirectory(JavaStoragePath);
    }

    public async Task<string> DownloadJDK(int majorVersion)
    {
        string targetDir = Path.Combine(JavaStoragePath, $"jdk-{majorVersion}");
        if (Directory.Exists(targetDir) && File.Exists(JavaHelper.GetBestJavaPath(majorVersion)))
        {
            // Check if it's already there (crude check)
            string exe = Path.Combine(targetDir, "bin", "java.exe");
            if (File.Exists(exe)) return exe;
        }

        try
        {
            EmitSignal(SignalName.DownloadProgress, $"Java {majorVersion}", 0.05f);

            // 1. Get download URL from Adoptium API
            string os = "windows";
            string arch = "x64";
            string apiUrl = $"https://api.adoptium.net/v3/assets/latest/{majorVersion}/hotspot?architecture={arch}&image_type=jdk&os={os}";

            string responseJson = await _httpClient.GetStringAsync(apiUrl);
            var doc = JsonDocument.Parse(responseJson);
            var binary = doc.RootElement.EnumerateArray().First().GetProperty("binary");
            string downloadUrl = binary.GetProperty("package").GetProperty("link").GetString();
            string fileName = binary.GetProperty("package").GetProperty("name").GetString();

            string zipPath = Path.Combine(JavaStoragePath, fileName);

            // 2. Download
            await DownloadFile(downloadUrl, zipPath, (p) => EmitSignal(SignalName.DownloadProgress, $"Java {majorVersion}", p, 0.1f + p * 0.8f));

            // 3. Extract
            EmitSignal(SignalName.DownloadProgress, $"Java {majorVersion}", 1.0f, 0.95f);
            await Task.Run(() =>
            {
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                ZipFile.ExtractToDirectory(zipPath, JavaStoragePath);

                // Adoptium ZIPs usually have a single folder inside like "jdk-21.0.1+12"
                // We want to rename it or find it.
                string extractedDir = Directory.GetDirectories(JavaStoragePath).FirstOrDefault(d => Path.GetFileName(d).StartsWith($"jdk-{majorVersion}") || Path.GetFileName(d).StartsWith($"jdk{majorVersion}"));
                if (extractedDir != null && extractedDir != targetDir)
                {
                    Directory.Move(extractedDir, targetDir);
                }

                File.Delete(zipPath);
            });

            string finalExe = Path.Combine(targetDir, "bin", "java.exe");
            EmitSignal(SignalName.DownloadFinished, $"Java {majorVersion}", finalExe);
            return finalExe;
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.DownloadFailed, $"Java {majorVersion}", e.Message);
            return null;
        }
    }

    public async Task<string> DownloadFabric(string mcVersion, string targetPath, string loaderVersion = "latest", string installerVersion = "latest")
    {
        try
        {
            string url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/{installerVersion}/server/jar";
            string fileName = $"fabric-server-launch.jar";
            string fullPath = Path.Combine(targetPath, fileName);

            await DownloadFile(url, fullPath, (p) => EmitSignal(SignalName.DownloadProgress, "Fabric Loader", p, p));
            EmitSignal(SignalName.DownloadFinished, "Fabric Loader", fullPath);
            return fullPath;
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.DownloadFailed, "Fabric Loader", e.Message);
            return null;
        }
    }

    public async Task<string> DownloadPaper(string mcVersion, string targetPath, string build = "latest")
    {
        try
        {
            // 1. Get latest build if not specified
            if (build == "latest")
            {
                string buildsUrl = $"https://api.papermc.io/v2/projects/paper/versions/{mcVersion}";
                string buildsJson = await _httpClient.GetStringAsync(buildsUrl);
                var doc = JsonDocument.Parse(buildsJson);
                var builds = doc.RootElement.GetProperty("builds").EnumerateArray();
                build = builds.Last().ToString();
            }

            // 2. Get file name
            string infoUrl = $"https://api.papermc.io/v2/projects/paper/versions/{mcVersion}/builds/{build}";
            string infoJson = await _httpClient.GetStringAsync(infoUrl);
            var infoDoc = JsonDocument.Parse(infoJson);
            string fileName = infoDoc.RootElement.GetProperty("downloads").GetProperty("application").GetProperty("name").GetString();

            // 3. Download
            string url = $"https://api.papermc.io/v2/projects/paper/versions/{mcVersion}/builds/{build}/downloads/{fileName}";
            string fullPath = Path.Combine(targetPath, fileName);

            await DownloadFile(url, fullPath, (p) => EmitSignal(SignalName.DownloadProgress, $"Paper {mcVersion}", p, p));
            EmitSignal(SignalName.DownloadFinished, $"Paper {mcVersion}", fullPath);
            return fullPath;
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.DownloadFailed, $"Paper {mcVersion}", e.Message);
            return null;
        }
    }

    public async Task<string> DownloadForge(string mcVersion, string targetPath, string forgeVersion)
    {
        try
        {
            EmitSignal(SignalName.DownloadProgress, "Forge Installer", 0.0f, 0.1f);

            // 1. Download installer
            string url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
            string installerPath = Path.Combine(targetPath, "forge-installer.jar");

            await DownloadFile(url, installerPath, (p) => EmitSignal(SignalName.DownloadProgress, "Forge Installer", p, 0.1f + p * 0.4f));

            // 2. Run installer
            EmitSignal(SignalName.DownloadProgress, "Installing Forge Server...", 0.0f, 0.6f);

            // Use JavaHelper to find a valid Java executable
            string javaPath = JavaHelper.GetBestJavaPath(17); // Most modern installers need 17+

            int exitCode = await Task.Run(() =>
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = $"-jar \"{installerPath}\" --installServer",
                    WorkingDirectory = targetPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var process = System.Diagnostics.Process.Start(startInfo);
                process.WaitForExit();
                return process.ExitCode;
            });

            if (exitCode != 0) throw new Exception($"Forge installer failed with exit code {exitCode}");

            // 3. Cleanup
            File.Delete(installerPath);
            string logFile = Path.Combine(targetPath, "forge-installer.jar.log");
            if (File.Exists(logFile)) File.Delete(logFile);

            // 4. Find the actual JAR to run
            // Forge changed its layout in newer versions (1.17+) - it uses a libraries folder and a run script
            // For older versions it still has a forge.jar
            string forgeJar = Directory.GetFiles(targetPath, "forge-*.jar")
                .FirstOrDefault(f => !f.Contains("installer"));

            if (string.IsNullOrEmpty(forgeJar))
            {
                // Newer Forge (1.17+) uses user_args.txt and a complex classpath
                // We'll need to handle this in ServerManager runner, but for now we'll mark success
                EmitSignal(SignalName.DownloadFinished, "Forge", targetPath);
                return "FORGE_INSTALLED";
            }

            EmitSignal(SignalName.DownloadFinished, "Forge", forgeJar);
            return forgeJar;
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.DownloadFailed, "Forge", e.Message);
            return null;
        }
    }

    public async Task<string> DownloadNeoForge(string mcVersion, string targetPath, string neoforgeVersion)
    {
        try
        {
            EmitSignal(SignalName.DownloadProgress, "NeoForge Installer", 0.0f, 0.1f);

            // 1. Download installer
            // NeoForge Maven: https://maven.neoforged.net/releases/net/neoforged/neoforge/20.1.0/neoforge-20.1.0-installer.jar
            // Note: NeoForge versions usually include the MC version prefix like 20.1.0 (for 1.20.1)
            string url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoforgeVersion}/neoforge-{neoforgeVersion}-installer.jar";
            string installerPath = Path.Combine(targetPath, "neoforge-installer.jar");

            await DownloadFile(url, installerPath, (p) => EmitSignal(SignalName.DownloadProgress, "NeoForge Installer", p, 0.1f + p * 0.4f));

            // 2. Run installer
            EmitSignal(SignalName.DownloadProgress, "Installing NeoForge Server...", 0.0f, 0.6f);

            string javaPath = JavaHelper.GetBestJavaPath(17);

            int exitCode = await Task.Run(() =>
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = $"-jar \"{installerPath}\" --installServer",
                    WorkingDirectory = targetPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var process = System.Diagnostics.Process.Start(startInfo);
                process.WaitForExit();
                return process.ExitCode;
            });

            if (exitCode != 0) throw new Exception($"NeoForge installer failed with exit code {exitCode}");

            // 3. Cleanup
            File.Delete(installerPath);

            EmitSignal(SignalName.DownloadFinished, "NeoForge", targetPath);
            return "NEOFORGE_INSTALLED";
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.DownloadFailed, "NeoForge", e.Message);
            return null;
        }
    }

    public async Task<List<string>> GetFabricLoaderVersions(string mcVersion)
    {
        try
        {
            string url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}";
            string json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                list.Add(item.GetProperty("loader").GetProperty("version").GetString());
            }
            return list;
        }
        catch { return new List<string> { "latest" }; }
    }

    public async Task<List<string>> GetPaperBuilds(string mcVersion)
    {
        try
        {
            string url = $"https://api.papermc.io/v2/projects/paper/versions/{mcVersion}";
            string json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            foreach (var build in doc.RootElement.GetProperty("builds").EnumerateArray())
            {
                list.Add(build.ToString());
            }
            list.Reverse();
            return list;
        }
        catch { return new List<string> { "latest" }; }
    }

    public async Task<List<string>> GetForgeVersions(string mcVersion)
    {
        try
        {
            string url = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
            string json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var promos = doc.RootElement.GetProperty("promos");

            var list = new List<string>();

            // Check for latest and recommended for this version
            if (promos.TryGetProperty($"{mcVersion}-latest", out var latest))
                list.Add(latest.GetString());
            if (promos.TryGetProperty($"{mcVersion}-recommended", out var recommended))
                if (!list.Contains(recommended.GetString()))
                    list.Add(recommended.GetString());

            // If empty, try to get anything for this version from maven (fallback)
            if (list.Count == 0) list.Add("latest");

            return list;
        }
        catch { return new List<string> { "latest" }; }
    }

    public async Task<List<string>> GetNeoForgeVersions(string mcVersion)
    {
        try
        {
            // NeoForge versioning: 1.20.1 -> 20.1.x, 1.21.1 -> 21.1.x
            string prefix = mcVersion;
            if (mcVersion.StartsWith("1.")) prefix = mcVersion.Substring(2);

            string url = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";
            string json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var versionsArray = doc.RootElement.GetProperty("versions").EnumerateArray();

            var list = new List<string>();
            foreach (var version in versionsArray)
            {
                string v = version.GetString();
                if (v.StartsWith(prefix + "."))
                {
                    list.Add(v);
                }
            }

            list.Reverse(); // Newest first
            return list.Take(10).ToList(); // Just top 10 for dropdown
        }
        catch { return new List<string> { "latest" }; }
    }

    private async Task DownloadFile(string url, string path, Action<float> progressCallback)
    {
        using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                var totalRead = 0L;
                var bytesRead = 0;
                var lastReportedPercent = -1;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (canReportProgress)
                    {
                        // Only report when percentage changes by at least 1%
                        int currentPercent = (int)((float)totalRead / totalBytes * 100);
                        if (currentPercent > lastReportedPercent)
                        {
                            lastReportedPercent = currentPercent;
                            progressCallback((float)totalRead / totalBytes);
                        }
                    }
                }
            }
        }
    }
}
