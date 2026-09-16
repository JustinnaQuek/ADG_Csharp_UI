using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmuMultichannelUi;

/// <summary>
/// Domain model for a single SMU channel.
/// </summary>
public class Channel : INotifyPropertyChanged
{
    private bool _isOutputEnabled;
    private double _setVoltage;
    private double _complianceCurrentLimit = 0.1; // amps
    private double _measuredVoltage;
    private double _measuredCurrent;
    private bool _isInCompliance;

    public int ChannelNumber { get; init; }

    public string ChannelName => $"Channel {ChannelNumber}";

    public bool IsOutputEnabled
    {
        get => _isOutputEnabled;
        set => SetField(ref _isOutputEnabled, value);
    }

    public double SetVoltage
    {
        get => _setVoltage;
        set => SetField(ref _setVoltage, value);
    }

    public double ComplianceCurrentLimit
    {
        get => _complianceCurrentLimit;
        set => SetField(ref _complianceCurrentLimit, value);
    }

    public double MeasuredVoltage
    {
        get => _measuredVoltage;
        set
        {
            if (SetField(ref _measuredVoltage, value))
            {
                OnPropertyChanged(nameof(Resistance));
            }
        }
    }

    public double MeasuredCurrent
    {
        get => _measuredCurrent;
        set
        {
            if (SetField(ref _measuredCurrent, value))
            {
                OnPropertyChanged(nameof(Resistance));
            }
        }
    }

    /// <summary>Simple derived load resistance, V / I. Zero (or disabled,
    /// zero-current) channels report zero rather than dividing by zero.</summary>
    public double Resistance => MeasuredCurrent == 0 ? 0 : MeasuredVoltage / MeasuredCurrent;

    public bool IsInCompliance
    {
        get => _isInCompliance;
        set
        {
            if (SetField(ref _isInCompliance, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string Status => IsInCompliance ? "COMPLIANCE" : "Normal";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
