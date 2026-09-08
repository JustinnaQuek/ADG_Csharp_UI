namespace SmuMultichannelUi;

/// <summary>
/// Fakes what a real SMU instrument driver would report back over the bus.
/// No hardware/gRPC here - just enough behavior to make the mockup feel alive:
/// measured values drift toward their setpoints, and we flag compliance
/// when the current limit is exceeded.
/// </summary>
public class SmuSimulator
{
    private readonly Random _random = new();

    /// <summary>
    /// Advances one channel's simulated readback by one tick. Called
    /// periodically (see MainWindow's readback timer) for every channel.
    /// </summary>
    public void UpdateChannel(Channel channel)
    {
        if (!channel.IsOutputEnabled)
        {
            channel.MeasuredVoltage = 0;
            channel.MeasuredCurrent = 0;
            channel.IsInCompliance = false;
            return;
        }

        // Drift measured voltage toward the setpoint with a little noise.
        var noise = (_random.NextDouble() - 0.5) * 0.01;
        channel.MeasuredVoltage = channel.SetVoltage + noise;

        // Simulate a load: assume a nominal 100-ohm load draws current
        // proportional to the applied voltage.
        var nominalLoadOhms = 100.0;
        channel.MeasuredCurrent = channel.MeasuredVoltage / nominalLoadOhms;

        channel.IsInCompliance = channel.MeasuredCurrent >= channel.ComplianceCurrentLimit;
    }
}
