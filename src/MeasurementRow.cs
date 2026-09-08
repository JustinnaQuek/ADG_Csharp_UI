namespace SmuMultichannelUi;

/// <summary>
/// A single row in the measurement statistics table - purely a display
/// record, computed from a channel's rolling sample history.
/// </summary>
public class MeasurementRow
{
    public string Channel { get; set; } = string.Empty;
    public string Measurement { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Mean { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public double StatisticalRange { get; set; }
    public double StdDeviation { get; set; }
    public int Count { get; set; }
}
