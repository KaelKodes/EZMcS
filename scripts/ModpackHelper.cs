using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

public partial class ModpackHelper : Node
{
    [Signal] public delegate void DownloadProgressEventHandler(float percent);
    [Signal] public delegate void DownloadFinishedEventHandler(string path);
    [Signal] public delegate void DownloadErrorEventHandler(string message);

    public async void DownloadModpack(string url, string targetDir)
    {
        try
        {
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient())
            {
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var totalBytesRead = 0L;
                var buffer = new byte[8192];
                var isMoreToRead = true;
                var lastReportedPercent = -1;

                string tempFile = Path.Combine(targetDir, "modpack_temp.zip");

                using (var fileStream = new FileStream(tempFile, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 8192, true))
                {
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        do
                        {
                            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (bytesRead == 0)
                            {
                                isMoreToRead = false;
                                continue;
                            }

                            await fileStream.WriteAsync(buffer, 0, bytesRead);

                            totalBytesRead += bytesRead;
                            if (totalBytes != -1)
                            {
                                // Only report when percentage changes by at least 1%
                                int currentPercent = (int)((float)totalBytesRead / totalBytes * 100);
                                if (currentPercent > lastReportedPercent)
                                {
                                    lastReportedPercent = currentPercent;
                                    EmitSignal(SignalName.DownloadProgress, (float)totalBytesRead / totalBytes);
                                }
                            }
                        }
                        while (isMoreToRead);
                    }
                }

                EmitSignal(SignalName.DownloadFinished, tempFile);
                ExtractModpack(tempFile, targetDir);
            }
        }
        catch (Exception ex)
        {
            EmitSignal(SignalName.DownloadError, ex.Message);
        }
    }

    private void ExtractModpack(string zipPath, string targetDir)
    {
        try
        {
            ZipFile.ExtractToDirectory(zipPath, targetDir, true);
            File.Delete(zipPath);
            GD.Print("Modpack extracted to: " + targetDir);
        }
        catch (Exception ex)
        {
            EmitSignal(SignalName.DownloadError, "Extraction failed: " + ex.Message);
        }
    }
}
