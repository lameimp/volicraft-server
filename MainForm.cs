using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolicraftLauncher;

internal sealed class MainForm : Form
{
    private const string ServerName = "Volicraft";
    private const string DefaultRealmAddress = "127.0.0.1";
    private const int AuthPort = 3724;
    private const int WorldPort = 8085;
    private const string LatestReleaseApi = "https://api.github.com/repos/lameimp/volicraft-server/releases/latest";
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly TextBox _clientPath = new();
    private readonly TextBox _serverAddress = new();
    private readonly Label _status = new();
    private readonly Label _tailscaleStatus = new();
    private readonly Button _configure = new();
    private readonly Button _launch = new();
    private readonly Button _refreshNetwork = new();
    private readonly Button _verifyFiles = new();
    private readonly Button _updateFiles = new();
    private readonly string _settingsPath;

    public MainForm()
    {
        Text = $"{ServerName} Launcher";
        Width = 620;
        Height = 300;
        MinimumSize = new Size(620, 300);
        StartPosition = FormStartPosition.CenterScreen;

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VolicraftLauncher",
            "settings.json");

        _clientPath.PlaceholderText = @"Select your WoW 3.3.5a folder";
        _serverAddress.Text = DefaultRealmAddress;
        _status.AutoSize = false;
        _status.Height = 55;
        _status.Dock = DockStyle.Fill;
        _status.Text = "Select your existing WoW 3.3.5a folder to begin.";
        _tailscaleStatus.AutoSize = true;
        _tailscaleStatus.Text = "Tailscale: checking...";

        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) => BrowseForClient();

        _configure.Text = "Configure Client";
        _configure.AutoSize = true;
        _configure.Click += async (_, _) => await ConfigureClientAsync();

        _launch.Text = "Launch WoW";
        _launch.AutoSize = true;
        _launch.Enabled = false;
        _launch.Click += (_, _) => LaunchClient();

        _refreshNetwork.Text = "Refresh Network";
        _refreshNetwork.AutoSize = true;
        _refreshNetwork.Click += async (_, _) => await RefreshNetworkStatusAsync();

        _verifyFiles.Text = "Verify Custom Files";
        _verifyFiles.AutoSize = true;
        _verifyFiles.Click += async (_, _) => await VerifyCustomFilesAsync();

        _updateFiles.Text = "Update Custom Files";
        _updateFiles.AutoSize = true;
        _updateFiles.Click += async (_, _) => await UpdateCustomFilesAsync();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "WoW folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_clientPath, 1, 0);
        layout.Controls.Add(browse, 2, 0);
        layout.Controls.Add(new Label { Text = "Server address:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_serverAddress, 1, 1);
        layout.SetColumnSpan(_serverAddress, 2);
        layout.Controls.Add(new Label { Text = "Ports:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(new Label { Text = $"{AuthPort} (login), {WorldPort} (world)", AutoSize = true }, 1, 2);
        layout.Controls.Add(_configure, 1, 3);
        layout.Controls.Add(_launch, 2, 3);
        layout.Controls.Add(_refreshNetwork, 1, 4);
        layout.Controls.Add(_tailscaleStatus, 2, 4);
        layout.Controls.Add(_verifyFiles, 1, 5);
        layout.Controls.Add(_updateFiles, 2, 5);
        layout.Controls.Add(_status, 0, 6);
        layout.SetColumnSpan(_status, 3);

        Controls.Add(layout);
        LoadSettings();
        FormClosed += (_, _) => SaveSettings();
        Shown += async (_, _) => await RefreshNetworkStatusAsync();
    }

    private void BrowseForClient()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing Wow.exe",
            UseDescriptionForTitle = true,
            SelectedPath = _clientPath.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _clientPath.Text = dialog.SelectedPath;
            _launch.Enabled = false;
            SetStatus("Folder selected. Click Configure Client to validate and write the realm list.");
        }
    }

    private async Task ConfigureClientAsync()
    {
        SetBusy(true);
        try
        {
            var clientRoot = ValidateClientRoot();
            var address = ValidateServerAddress();
            SetStatus("Checking the server ports...");

            var authReachable = await CanConnectAsync(address, AuthPort);
            var worldReachable = await CanConnectAsync(address, WorldPort);
            if (!authReachable || !worldReachable)
            {
                SetStatus($"Could not reach {ServerName}. Login: {authReachable}, world: {worldReachable}. Check Tailscale and the server.");
                return;
            }

            var realmList = FindRealmList(clientRoot);
            var backup = $"{realmList}.volicraft-backup-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(realmList, backup);
            File.WriteAllText(realmList, $"set realmlist {address}{Environment.NewLine}", new UTF8Encoding(false));
            SaveSettings();
            _launch.Enabled = true;
            SetStatus($"Configured successfully. Original realmlist backed up to {Path.GetFileName(backup)}.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message);
            _launch.Enabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshNetworkStatusAsync()
    {
        SetBusy(true);
        try
        {
            var address = ValidateServerAddress();
            var tailscale = await GetTailscaleStatusAsync();
            if (!tailscale.Installed)
            {
                _tailscaleStatus.Text = "Tailscale: not installed";
                SetStatus("Install Tailscale through the official installer, then sign in.");
                return;
            }

            if (!tailscale.SignedIn)
            {
                _tailscaleStatus.Text = "Tailscale: installed, not signed in";
                SetStatus("Open Tailscale and sign in before connecting to Volicraft.");
                return;
            }

            _tailscaleStatus.Text = $"Tailscale: signed in{(string.IsNullOrWhiteSpace(tailscale.Ip) ? string.Empty : $" ({tailscale.Ip})")}";
            var authReachable = await CanConnectAsync(address, AuthPort);
            var worldReachable = await CanConnectAsync(address, WorldPort);
            SetStatus($"Volicraft ports: login {(authReachable ? "reachable" : "unreachable")}, world {(worldReachable ? "reachable" : "unreachable")}.");
        }
        catch (ArgumentException ex)
        {
            _tailscaleStatus.Text = "Tailscale: not checked";
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task VerifyCustomFilesAsync()
    {
        SetBusy(true);
        try
        {
            var clientRoot = ValidateClientRoot();
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "volicraft-manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("The launcher manifest is missing.");

            var manifest = JsonSerializer.Deserialize<VolicraftManifest>(
                await File.ReadAllTextAsync(manifestPath), JsonOptions);
            if (manifest?.Files is null || manifest.Files.Count == 0)
                throw new InvalidDataException("The launcher manifest contains no files.");

            var failures = new List<string>();
            foreach (var entry in manifest.Files)
            {
                var relativePath = entry.Path.Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(clientRoot, relativePath);
                if (!File.Exists(filePath))
                {
                    failures.Add($"{entry.Path}: missing");
                    continue;
                }

                await using var stream = File.OpenRead(filePath);
                var actualHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
                if (!actualHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"{entry.Path}: hash mismatch");
            }

            if (failures.Count > 0)
            {
                _launch.Enabled = false;
                SetStatus($"Custom files need repair: {string.Join(", ", failures)}");
                return;
            }

            SetStatus($"Verified {manifest.Files.Count} Volicraft custom file(s).");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _launch.Enabled = false;
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateCustomFilesAsync()
    {
        SetBusy(true);
        try
        {
            var clientRoot = ValidateClientRoot();
            SetStatus("Checking GitHub for the latest Volicraft release...");
            var release = await GetLatestReleaseAsync();
            var manifestAsset = release.Assets.FirstOrDefault(asset =>
                asset.Name.Equals("volicraft-manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is null)
                throw new InvalidDataException("The latest GitHub Release has no manifest asset.");

            var manifest = JsonSerializer.Deserialize<VolicraftManifest>(
                await DownloadTextAsync(manifestAsset.BrowserDownloadUrl), JsonOptions);
            if (manifest?.Files is null || manifest.Files.Count == 0)
                throw new InvalidDataException("The release manifest contains no files.");

            var tempRoot = Path.Combine(Path.GetTempPath(), "VolicraftLauncher", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                foreach (var entry in manifest.Files)
                {
                    if (!IsAllowedCustomFile(entry.Path))
                        throw new InvalidDataException($"Manifest contains an unsupported path: {entry.Path}");

                    var asset = release.Assets.FirstOrDefault(candidate =>
                        candidate.Name.Equals(Path.GetFileName(entry.Path), StringComparison.OrdinalIgnoreCase));
                    if (asset is null)
                        throw new InvalidDataException($"The release is missing {Path.GetFileName(entry.Path)}.");

                    var tempFile = Path.Combine(tempRoot, Path.GetFileName(entry.Path));
                    await DownloadFileAsync(asset.BrowserDownloadUrl, tempFile);
                    await VerifyHashAsync(tempFile, entry.Sha256, entry.Path);
                    await InstallVerifiedFileAsync(clientRoot, entry.Path, tempFile);
                }
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }

            SetStatus($"Updated {manifest.Files.Count} custom file(s) from release {release.TagName}.");
            _launch.Enabled = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException)
        {
            _launch.Enabled = false;
            SetStatus($"Update failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LaunchClient()
    {
        try
        {
            var clientRoot = ValidateClientRoot();
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(clientRoot, "Wow.exe"),
                WorkingDirectory = clientRoot,
                UseShellExecute = true
            });
            SetStatus("WoW launched.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message);
        }
    }

    private string ValidateClientRoot()
    {
        var root = _clientPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new ArgumentException("Select a valid WoW client folder.");
        if (!File.Exists(Path.Combine(root, "Wow.exe")))
            throw new ArgumentException("The selected folder does not contain Wow.exe.");
        return root;
    }

    private string FindRealmList(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "Data", "enUS", "realmlist.wtf"),
            Path.Combine(root, "Data", "enGB", "realmlist.wtf")
        };
        var existing = candidates.FirstOrDefault(File.Exists);
        if (existing is null)
            throw new FileNotFoundException("Could not find Data\\enUS\\realmlist.wtf or Data\\enGB\\realmlist.wtf.");
        return existing;
    }

    private string ValidateServerAddress()
    {
        var address = _serverAddress.Text.Trim();
        if (string.IsNullOrWhiteSpace(address) || address.Contains(' ') || address.Contains(';'))
            throw new ArgumentException("Enter a hostname or IP address without spaces.");
        return address;
    }

    private static async Task<bool> CanConnectAsync(string host, int port)
    {
        using var client = new TcpClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(host, port, cancellation.Token);
            return true;
        }

        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VolicraftLauncher/1.0");
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<GitHubRelease> GetLatestReleaseAsync()
    {
        await using var stream = await Http.GetStreamAsync(LatestReleaseApi);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream);
        return release ?? throw new InvalidDataException("GitHub returned an empty release response.");
    }

    private static Task<string> DownloadTextAsync(string url) => Http.GetStringAsync(url);

    private static async Task DownloadFileAsync(string url, string destination)
    {
        await using var source = await Http.GetStreamAsync(url);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target);
    }

    private static async Task VerifyHashAsync(string path, string expectedHash, string displayPath)
    {
        await using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Hash verification failed for {displayPath}.");
    }

    private static async Task InstallVerifiedFileAsync(string clientRoot, string relativePath, string source)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(clientRoot, normalized));
        var root = Path.GetFullPath(clientRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Manifest path is outside the client folder: {relativePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
            File.Move(destination, $"{destination}.volicraft-backup-{DateTime.Now:yyyyMMdd-HHmmss}");

        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output);
    }

    private static bool IsAllowedCustomFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Equals("Data/enUS/patch-enUS-4.MPQ", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Data/enUS/patch-enUS-5.MPQ", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TailscaleStatus> GetTailscaleStatusAsync()
    {
        var executable = FindTailscaleExecutable();
        if (executable is null)
            return new TailscaleStatus(false, false, null);

        var status = await RunProcessAsync(executable, "status");
        if (status.ExitCode != 0 || status.Output.Contains("NoState", StringComparison.OrdinalIgnoreCase))
            return new TailscaleStatus(true, false, null);

        var ipResult = await RunProcessAsync(executable, "ip -4");
        var ip = ipResult.ExitCode == 0 ? ipResult.Output.Trim() : null;
        return new TailscaleStatus(true, true, ip);
    }

    private static string? FindTailscaleExecutable()
    {
        var knownPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (File.Exists(knownPath))
            return knownPath;

        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder, "tailscale.exe"))
            .FirstOrDefault(File.Exists);
        return path;
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, string.IsNullOrWhiteSpace(output) ? error : output);
    }

    private void SetBusy(bool busy)
    {
        _configure.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void SetStatus(string message) => _status.Text = message;

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsPath));
            if (settings is null)
                return;
            _clientPath.Text = settings.ClientPath ?? string.Empty;
            _serverAddress.Text = string.IsNullOrWhiteSpace(settings.ServerAddress)
                ? DefaultRealmAddress
                : settings.ServerAddress;
        }
        catch (JsonException)
        {
            SetStatus("Saved launcher settings were invalid; please select the client folder again.");
        }
        catch (IOException)
        {
            SetStatus("Saved launcher settings could not be read.");
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var settings = new LauncherSettings(_clientPath.Text.Trim(), _serverAddress.Text.Trim());
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // A settings failure must not prevent the launcher or game from closing.
        }
    }

    private sealed record LauncherSettings(string? ClientPath, string? ServerAddress);
    private sealed record TailscaleStatus(bool Installed, bool SignedIn, string? Ip);
    private sealed record ProcessResult(int ExitCode, string Output);
    private sealed record VolicraftManifest(int Version, List<ManifestFile> Files);
    private sealed record ManifestFile(string Path, string Sha256);
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);
    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
