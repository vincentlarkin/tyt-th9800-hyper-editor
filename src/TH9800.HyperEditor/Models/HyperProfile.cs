using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TH9800.HyperEditor.Models;

public sealed class HyperProfile : INotifyPropertyChanged
{
    private int _leftChannel;
    private int _rightChannel;
    private bool _beep;
    private int _backlight;
    private int _p1;
    private int _p2;
    private int _p3;
    private int _p4;

    public required char Slot { get; init; }
    public required int ImageOffset { get; init; }
    public string Address => $"0x{ImageOffset + 0xF0:X4}";

    public int LeftChannel { get => _leftChannel; set => Set(ref _leftChannel, value); }
    public int RightChannel { get => _rightChannel; set => Set(ref _rightChannel, value); }
    public bool Beep { get => _beep; set => Set(ref _beep, value); }
    public int Backlight
    {
        get => _backlight;
        set
        {
            var previous = _backlight;
            Set(ref _backlight, value);
            if (previous != _backlight) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BacklightLabel)));
        }
    }
    public string BacklightLabel => Backlight == 0 ? "Off" : $"Level {Backlight}";
    public int P1 { get => _p1; set => Set(ref _p1, value); }
    public int P2 { get => _p2; set => Set(ref _p2, value); }
    public int P3 { get => _p3; set => Set(ref _p3, value); }
    public int P4 { get => _p4; set => Set(ref _p4, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
