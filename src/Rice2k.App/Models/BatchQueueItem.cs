using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rice2k.Encryption.Models;

public sealed class BatchQueueItem : INotifyPropertyChanged
{
    private string _status = "Waiting";
    private double _progress;
    private string _outputPath = string.Empty;

    public BatchQueueItem(string sourcePath)
    {
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }
    public string FileName => Path.GetFileName(SourcePath);
    public long SizeBytes => File.Exists(SourcePath) ? new FileInfo(SourcePath).Length : 0;
    public string SizeDisplay => FormatBytes(SizeBytes);

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_progress - clamped) < 0.01)
                return;
            _progress = clamped;
            OnPropertyChanged();
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (_outputPath == value)
                return;
            _outputPath = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
