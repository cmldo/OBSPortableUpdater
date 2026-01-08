using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OBSPortableUpdater
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        Button btnCheck, btnUpdate;
        ProgressBar progress;
        TextBox log;

        readonly string root;
        readonly string obsDir;
        readonly string obsExe;
        // readonly string tempDir;
        string? assetUrl;

        Version? installedVersion;
        Version? latestVersion;

        const string API =
            "https://api.github.com/repos/obsproject/obs-studio/releases/latest";

        public MainForm()
        {
            Text = "OBS Portable Updater";
            Width = 640;
            Height = 420;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            btnCheck = new Button { Text = "Check version", Left = 20, Top = 20, Width = 160 };
            btnUpdate = new Button { Text = "Update OBS", Left = 200, Top = 20, Width = 120, Enabled = false };
            progress = new ProgressBar { Left = 20, Top = 60, Width = 580 };
            log = new TextBox { Left = 20, Top = 95, Width = 580, Height = 260, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            Controls.AddRange(new Control[] { btnCheck, btnUpdate, progress, log });

            btnCheck.Click += async (_, __) => await CheckLatestAsync();
            btnUpdate.Click += async (_, __) => await UpdateObsAsync();

            root = AppDomain.CurrentDomain.BaseDirectory;
            obsDir = Path.Combine(root, "obs-studio");
            obsExe = Path.Combine(obsDir, "bin", "64bit", "obs64.exe");
            //tempDir = Path.Combine(root, "_temp");
        }

        void Log(string msg)
        {
            log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            log.SelectionStart = log.Text.Length;
            log.ScrollToCaret();
            Application.DoEvents();
        }

        void HardFail(string msg)
        {
            Log("FATAL: " + msg);
            MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw new InvalidOperationException(msg);
        }

        Version GetInstalledVersion()
        {
            bool obsExists = File.Exists(obsExe);

if (!obsExists)
{
    Log("OBS not found. Downloading latest version...");
    await DownloadAndInstallLatestAsync();
    return;
}
            var info = FileVersionInfo.GetVersionInfo(obsExe);
            return Version.Parse(info.ProductVersion);
        }

        async Task CheckLatestAsync()
        {
            try
            {
                progress.Value = 10;
                Log("Checking installed OBS version...");
                installedVersion = GetInstalledVersion();
                Log($"Installed version: {installedVersion}");

                progress.Value = 30;
                Log("Checking latest GitHub release...");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OBSPortableUpdater");
                var json = await client.GetStringAsync(API);
                using var doc = JsonDocument.Parse(json);

                var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
                latestVersion = Version.Parse(tag.TrimStart('v'));

                var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                    .FirstOrDefault(a => a.GetProperty("name").GetString()!.EndsWith("-Windows-x64.zip"));

                if (asset.ValueKind == JsonValueKind.Undefined) HardFail("Could not find Windows x64 asset.");
                assetUrl = asset.GetProperty("browser_download_url").GetString();

                Log($"Latest version: {latestVersion}");
                progress.Value = 100;

                if (installedVersion < latestVersion)
                {
                    Log("Update available 🚀");
                    btnUpdate.Enabled = true;
                }
                else Log("OBS is up to date ✔");
            }
            catch (Exception ex)
            {
                HardFail(ex.Message);
            }
        }

        async Task UpdateObsAsync()
        {
            if (assetUrl == null || latestVersion == null) HardFail("No update info.");

            try
            {
                btnUpdate.Enabled = false;
                progress.Value = 0;

                string zipFileName = Path.GetFileName(assetUrl!);
                string zipPath = Path.Combine(root, zipFileName);

                if (File.Exists(zipPath)) {
                    Log($"Deleting existing {zipFileName}...");
                    File.Delete(zipPath);
                }

                Log($"Downloading {zipFileName}...");
                progress.Value = 20;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OBSPortableUpdater");
                var data = await client.GetByteArrayAsync(assetUrl);
                await File.WriteAllBytesAsync(zipPath, data);

                Log("Extracting...");
                progress.Value = 50;
                ZipFile.ExtractToDirectory(zipPath, obsDir, true);

                Log("Replacing obs-studio folder...");
                progress.Value = 70;
                Log($"Deleting {zipFileName}...");
                File.Delete(zipPath);

                progress.Value = 100;
                Log("OBS updated successfully ✔");
                MessageBox.Show("Update complete!", "Done");
            }
            catch (Exception ex)
            {
                HardFail("Update failed: " + ex.Message);
            }
        }
    }
}

