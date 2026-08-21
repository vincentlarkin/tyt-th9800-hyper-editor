using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TH9800.HyperEditor.Models;
using TH9800.HyperEditor.Services;

namespace TH9800.HyperEditor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RadioCloneProtocol _radio = new();
    private byte[]? _imageBytes;
    private string? _imagePath;
    private string? _selectedPort;
    private bool _busy;

    public ObservableCollection<HyperProfile> Profiles { get; } = [];
    public ObservableCollection<string> Ports { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];
    public ObservableCollection<ChannelOption> ChannelOptions { get; } = [];
    public IReadOnlyList<KeyOption> KeyOptions { get; } =
    [
        new(0, "OFF", "No assigned action"),
        new(1, "SCAN", "Start Main-band scanning"),
        new(2, "SQL.OFF", "Open Main-band squelch"),
        new(3, "TCALL", "Send the selected tone burst"),
        new(4, "RPTR", "Select repeater shift"),
        new(5, "PRI", "Priority monitoring"),
        new(6, "LOW", "Select transmit power"),
        new(7, "TONE", "Open CTCSS/DCS controls"),
        new(8, "MHz", "Tune in 1 MHz steps"),
        new(9, "REV", "Reverse repeater TX/RX"),
        new(10, "HOME", "Recall the HOME channel"),
        new(11, "BAND", "Move Main left/right"),
        new(12, "VFO/MR", "Switch VFO / Memory")
    ];

    public string? SelectedPort
    {
        get => _selectedPort;
        set { _selectedPort = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ReplaceChannels(ChannelCatalog.Empty());
        RefreshPorts();
        TryAutoLoadChannelLabels();
        TryAutoLoadImage();
        Log("Studio ready. No radio command has been sent.");
    }

    private async void ReadRadio_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePort()) return;
        await RunBusyAsync("Reading the complete radio image…", async () =>
        {
            var progress = new Progress<int>(value => Progress.Value = value);
            var bytes = await Task.Run(() => _radio.ReadImage(SelectedPort!, progress));
            var path = SaveBackup(bytes, "READ");
            LoadImage(bytes, path);
            Log($"Radio read completed and backed up: {Path.GetFileName(path)}");
        });
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a TH-9800 raw clone image",
            Filter = "Raw clone images (*.bin;*.img)|*.bin;*.img|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            LoadImage(File.ReadAllBytes(dialog.FileName), dialog.FileName);
            Log($"Opened image: {dialog.FileName}");
        }
        catch (Exception exception) { ShowError("Could not open image", exception); }
    }

    private void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bytes = BuildEditedImage();
            var dialog = new SaveFileDialog
            {
                Title = "Save edited TH-9800 image",
                Filter = "Raw clone image (*.bin)|*.bin",
                FileName = $"TH9800 HYPER EDITED {DateTime.Now:yyyy-MM-dd HHmmss}.bin"
            };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllBytes(dialog.FileName, bytes);
            LoadImage(bytes, dialog.FileName);
            Log($"Saved edited copy: {dialog.FileName}");
        }
        catch (Exception exception) { ShowError("Could not save image", exception); }
    }

    private void ImportChannels_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import channel names from CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ReplaceChannels(ChannelCatalog.Load(dialog.FileName));
            Log($"Imported channel labels: {dialog.FileName}");
        }
        catch (Exception exception) { ShowError("Could not import channel labels", exception); }
    }

    private void BeepOffAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var profile in Profiles) profile.Beep = false;
        Log("Staged BEEP OFF in all six Hyper profiles. No radio write yet.");
    }

    private void FactoryKeys_Click(object sender, RoutedEventArgs e)
    {
        foreach (var profile in Profiles)
        {
            profile.P1 = 11;
            profile.P2 = 12;
            profile.P3 = 7;
            profile.P4 = 6;
        }
        Log("Staged factory mic-key defaults in A–F: BAND / VFO-MR / TONE / LOW.");
    }

    private void DiscardStaged_Click(object sender, RoutedEventArgs e)
    {
        if (_imageBytes is null)
        {
            MessageBox.Show(this, "There is no loaded image to restore.", "Nothing loaded",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadImage(_imageBytes, _imagePath ?? "Loaded radio image");
        Log("Discarded all staged profile edits and restored the loaded image.");
    }

    private async void WriteVerify_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePort()) return;
        byte[] edited;
        try { edited = BuildEditedImage(); }
        catch (Exception exception) { ShowError("Cannot prepare write", exception); return; }

        var changed = HyperImage.DifferentOffsets(_imageBytes!, edited);
        if (changed.Count == 0)
        {
            MessageBox.Show(this, "There are no staged changes to write.", "Nothing to write", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Write {changed.Count:N0} changed byte(s) through the full clone protocol on {SelectedPort}?\n\n" +
            "The app will first capture a safety backup, then write, read the radio again, and require an exact SHA-256 match. Do not interrupt power.",
            "Confirm write + verify", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        await RunBusyAsync("Reading a pre-write safety backup…", async () =>
        {
            var progress = new Progress<int>(value => Progress.Value = value);
            var before = await Task.Run(() => _radio.ReadImage(SelectedPort!, progress));
            var backupPath = SaveBackup(before, "PRE-WRITE");
            Log($"Safety backup saved: {backupPath}");

            SetStatus("Writing the edited image…", 0);
            await Task.Run(() => _radio.WriteImage(SelectedPort!, edited, progress));
            Log("All write blocks were acknowledged.");

            var after = await ReadBackAfterWriteAsync(SelectedPort!, progress);
            var readbackPath = SaveBackup(after, "POST-WRITE");
            var expectedHash = Convert.ToHexString(SHA256.HashData(edited));
            var actualHash = Convert.ToHexString(SHA256.HashData(after));
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(edited), SHA256.HashData(after)))
                throw new InvalidDataException($"Readback mismatch. The safety backup is {backupPath}; readback is {readbackPath}.\nExpected {expectedHash}\nActual   {actualHash}");

            LoadImage(after, readbackPath);
            Log("Verified readback loaded as the clean baseline. Staged changes cleared.");
            Log($"WRITE VERIFIED: {actualHash}");
            MessageBox.Show(this,
                "The post-write readback matched byte-for-byte. Press a Hyper key on the radio to recall its updated profile.",
                "Write verified", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async Task<byte[]> ReadBackAfterWriteAsync(string portName, IProgress<int> progress)
    {
        const int maximumAttempts = 4;
        var restartDelay = TimeSpan.FromSeconds(3);
        var retryDelay = TimeSpan.FromSeconds(2);

        SetStatus("Radio is restarting before verification…", 0);
        Log("Write complete. Waiting 3 seconds for the radio to become ready for verification.");
        await Task.Delay(restartDelay);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                SetStatus($"Reading back for byte-for-byte verification (attempt {attempt}/{maximumAttempts})…", 0);
                return await Task.Run(() => _radio.ReadImage(portName, progress));
            }
            catch (Exception exception) when (attempt < maximumAttempts && IsTransientRadioReadyError(exception))
            {
                Log($"Verification connection attempt {attempt}/{maximumAttempts} found the radio still restarting. Retrying in 2 seconds.");
                SetStatus("Radio is not ready yet; verification will retry…", 0);
                await Task.Delay(retryDelay);
            }
        }
    }

    private static bool IsTransientRadioReadyError(Exception exception) =>
        exception is TimeoutException or IOException or UnauthorizedAccessException;

    private byte[] BuildEditedImage()
    {
        if (_imageBytes is null) throw new InvalidOperationException("Read the radio or open an image first.");
        return HyperImage.FromBytes(_imageBytes).ApplyProfiles(Profiles);
    }

    private void LoadImage(byte[] bytes, string path)
    {
        var image = HyperImage.FromBytes(bytes);
        var loadedProfiles = image.ReadProfiles();
        var correctedDimmers = loadedProfiles
            .Where(profile => profile.Backlight is < 0 or > 4)
            .Select(profile => (profile.Slot, Original: profile.Backlight))
            .ToList();
        foreach (var profile in loadedProfiles.Where(profile => profile.Backlight is < 0 or > 4))
            profile.Backlight = 4;

        _imageBytes = image.ToArray();
        _imagePath = path;
        foreach (var existing in Profiles) existing.PropertyChanged -= Profile_PropertyChanged;
        Profiles.Clear();
        foreach (var profile in loadedProfiles)
        {
            profile.PropertyChanged += Profile_PropertyChanged;
            Profiles.Add(profile);
        }
        var active = image.ReadActiveProfile();
        ImageBadge.Text = "NT_9800 • 65,296 BYTES";
        ImagePathText.Text = path;
        HashText.Text = $"SHA-256\n{image.Sha256[..32]}…";
        ActiveText.Text = $"Active: CH {active.LeftChannel} / CH {active.RightChannel}";
        SetStatus("Image validated. Edit safely or write with verification.", 0);
        UpdateStagedState();
        foreach (var correction in correctedDimmers)
            Log($"Hyper {correction.Slot} contained unsupported DIMMER value {correction.Original}; staged safe maximum 4.");
    }

    private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateStagedState();

    private void UpdateStagedState()
    {
        if (_imageBytes is null || Profiles.Count == 0)
        {
            StagedText.Text = "No image loaded";
            return;
        }

        try
        {
            var count = HyperImage.DifferentOffsets(_imageBytes, BuildEditedImage()).Count;
            if (count == 0)
            {
                StagedText.Text = "No staged changes";
                StagedText.Foreground = (Brush)FindResource("SuccessBrush");
                StagedDot.Fill = (Brush)FindResource("SuccessBrush");
                StagedBadge.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF3, 0xEF));
                StagedBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xD9, 0xCC));
            }
            else
            {
                StagedText.Text = $"{count:N0} staged byte change{(count == 1 ? string.Empty : "s")}";
                StagedText.Foreground = (Brush)FindResource("AccentDarkBrush");
                StagedDot.Fill = (Brush)FindResource("AccentBrush");
                StagedBadge.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE2));
                StagedBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xB4, 0x9F));
            }
        }
        catch
        {
            StagedText.Text = "Check staged values";
        }
    }

    private async Task RunBusyAsync(string status, Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        ReadButton.IsEnabled = false;
        WriteButton.IsEnabled = false;
        SetStatus(status, 0);
        try
        {
            await operation();
            Progress.Value = 100;
        }
        catch (Exception exception)
        {
            Log($"ERROR: {exception.Message}");
            ShowError("Operation failed", exception);
        }
        finally
        {
            _busy = false;
            ReadButton.IsEnabled = true;
            WriteButton.IsEnabled = true;
        }
    }

    private bool RequirePort()
    {
        if (!string.IsNullOrWhiteSpace(SelectedPort)) return true;
        MessageBox.Show(this, "Select the radio's COM port first.", "No COM port", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void RefreshPorts()
    {
        var previous = SelectedPort;
        Ports.Clear();
        foreach (var port in RadioCloneProtocol.Ports()) Ports.Add(port);
        SelectedPort = previous is not null && Ports.Contains(previous)
            ? previous
            : Ports.FirstOrDefault();
        Log($"Found {Ports.Count} serial port(s)." + (SelectedPort is null ? string.Empty : $" Selected {SelectedPort}."));
    }

    private void ReplaceChannels(IReadOnlyList<ChannelOption> channels)
    {
        ChannelOptions.Clear();
        foreach (var channel in channels) ChannelOptions.Add(channel);
    }

    private void TryAutoLoadChannelLabels()
    {
        try
        {
            var radioRoot = FindRadioRoot();
            if (radioRoot is null) return;
            var planDir = Path.Combine(radioRoot, "Channel Plans");
            var csv = Directory.Exists(planDir)
                ? new DirectoryInfo(planDir).EnumerateFiles("*MASTER.csv").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (csv is null) return;
            ReplaceChannels(ChannelCatalog.Load(csv.FullName));
            Log($"Auto-loaded channel labels: {csv.Name}");
        }
        catch (Exception exception) { Log($"Channel-label auto-load skipped: {exception.Message}"); }
    }

    private void TryAutoLoadImage()
    {
        try
        {
            var directory = BackupDirectory();
            if (!Directory.Exists(directory)) return;
            var latest = new DirectoryInfo(directory).EnumerateFiles("*.bin").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
            if (latest is null) return;
            LoadImage(File.ReadAllBytes(latest.FullName), latest.FullName);
            Log($"Auto-loaded latest verified/raw image: {latest.Name}");
        }
        catch (Exception exception) { Log($"Image auto-load skipped: {exception.Message}"); }
    }

    private string SaveBackup(byte[] bytes, string label)
    {
        var directory = BackupDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"TH9800 {label} {DateTime.Now:yyyy-MM-dd HHmmss}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string BackupDirectory()
    {
        var root = FindRadioRoot();
        return root is not null
            ? Path.Combine(root, "Backups", "Raw Hyper Research")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TH9800 Hyper Studio", "Backups");
    }

    private static string? FindRadioRoot()
    {
        var starts = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(start);
            for (var level = 0; level < 6 && directory is not null; level++, directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Channel Plans")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Backups")))
                    return directory.FullName;
            }
        }
        return null;
    }

    private void SetStatus(string text, int progress)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = text;
            Progress.Value = progress;
        });
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            Activity.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
            while (Activity.Count > 100) Activity.RemoveAt(Activity.Count - 1);
        });
    }

    private void ShowError(string title, Exception exception) =>
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public sealed record KeyOption(int Value, string Name, string Description);
}
