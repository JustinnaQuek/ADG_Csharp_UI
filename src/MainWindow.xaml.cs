using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Timer = System.Timers.Timer;
using ThreadingTimer = System.Threading.Timer;

namespace SmuMultichannelUi;

public partial class MainWindow : Window
{
    private const int ChannelCount = 4;
    private const int ReadbackIntervalMs = 500;
    private const int HistoryCapacity = 60;
    private const double VoltageAxisRange = 12.0; // graph shows +/- this many volts

    private static readonly Brush[] ChannelColors =
    {
        new SolidColorBrush(Color.FromRgb(0x29, 0xD3, 0xE0)), // cyan
        new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x29)), // orange
        new SolidColorBrush(Color.FromRgb(0xE0, 0x29, 0x9E)), // magenta
        new SolidColorBrush(Color.FromRgb(0x5B, 0xD9, 0x5B)), // green
    };

    private readonly SmuSimulator _simulator = new();
    private readonly Timer _readbackTimer = new(ReadbackIntervalMs);
    private ThreadingTimer? _complianceWatcher;

    private readonly Dictionary<int, Queue<double>> _voltageHistory = new();
    private readonly Dictionary<int, Queue<double>> _currentHistory = new();

    public ObservableCollection<Channel> Channels { get; } = new();
    public ObservableCollection<MeasurementRow> MeasurementRows { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        InitializeChannels();
        BuildChannelToggleButtons();
        BuildLegend();
    }

    private void InitializeChannels()
    {
        for (int i = 1; i <= ChannelCount; i++)
        {
            Channels.Add(new Channel { ChannelNumber = i });
            _voltageHistory[i] = new Queue<double>(HistoryCapacity);
            _currentHistory[i] = new Queue<double>(HistoryCapacity);
        }
    }

    private void BuildChannelToggleButtons()
    {
        Channel channel;
        for (int i = 0; i < Channels.Count; i++)
        {
            channel = Channels[i];

            var label = new TextBlock
            {
                Text = $"CH{channel.ChannelNumber} OUTPUT",
                Style = (Style)FindResource("OutputLabel"),
            };

            var toggleSwitch = new ToggleButton
            {
                Style = (Style)FindResource("OutputToggleSwitch"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            toggleSwitch.Click += (s, e) => channel.IsOutputEnabled = !channel.IsOutputEnabled;

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(toggleSwitch, Dock.Right);
            row.Children.Add(toggleSwitch);
            row.Children.Add(label);

            ChannelTogglePanel.Children.Add(row);
        }
    }

    private void BuildLegend()
    {
        for (int i = 0; i < Channels.Count; i++)
        {
            var swatch = new Rectangle { Width = 10, Height = 10, Fill = ChannelColors[i % ChannelColors.Length], Margin = new Thickness(0, 0, 4, 0) };
            var label = new TextBlock { Text = Channels[i].ChannelName, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 0, 14, 0) };
            var item = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(swatch);
            item.Children.Add(label);
            LegendPanel.Children.Add(item);
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _readbackTimer.Elapsed -= ReadbackTimer_Elapsed;
        _readbackTimer.Elapsed += ReadbackTimer_Elapsed;
        _readbackTimer.Start();

        StatusText.Text = "Running";
        StatusDot.Fill = Brushes.LimeGreen;

        // Watches for compliance trips and reflects it in the status header.
        _complianceWatcher = new ThreadingTimer(ComplianceWatcher_Tick, null, 1000, 1000);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _readbackTimer.Stop();
        _complianceWatcher?.Dispose();
        _complianceWatcher = null;

        StatusText.Text = "Idle";
        StatusDot.Fill = Brushes.Gray;
    }

    private void ReadbackTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // System.Timers.Timer.Elapsed fires on a ThreadPool thread - marshal
        // onto the UI thread before touching bound channels/UI elements.
        Dispatcher.Invoke(() =>
        {
            foreach (var channel in Channels)
            {
                _simulator.UpdateChannel(channel);

                var voltageQueue = _voltageHistory[channel.ChannelNumber];
                voltageQueue.Enqueue(channel.MeasuredVoltage);
                while (voltageQueue.Count > HistoryCapacity)
                {
                    voltageQueue.Dequeue();
                }

                var currentQueue = _currentHistory[channel.ChannelNumber];
                currentQueue.Enqueue(channel.MeasuredCurrent);
                while (currentQueue.Count > HistoryCapacity)
                {
                    currentQueue.Dequeue();
                }
            }

            RedrawGraph();
            RebuildMeasurementRows();
        });
    }

    /// <summary>
    /// Runs on a raw ThreadPool thread (via System.Threading.Timer, which has
    /// no UI-thread affinity at all) and pokes the sidebar status indicator
    /// directly - no Dispatcher marshaling. Reproduces a genuine cross-thread
    /// WPF violation once any channel trips its compliance limit.
    /// </summary>
    private void ComplianceWatcher_Tick(object? state)
    {
        bool anyTripped = Channels.Any(c => c.IsInCompliance);
        if (!anyTripped)
        {
            return;
        }

        StatusDot.Fill = Brushes.OrangeRed;
        StatusText.Text = "Compliance limit reached";
    }

    private void GraphHost_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawGraph();

    private void RedrawGraph()
    {
        GraphCanvas.Children.Clear();

        double width = GraphHost.ActualWidth;
        double height = GraphHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Horizontal gridlines + voltage-axis labels.
        const int gridLineCount = 6;
        for (int i = 0; i <= gridLineCount; i++)
        {
            double y = height * i / gridLineCount;
            var line = new Line { X1 = 0, Y1 = y, X2 = width, Y2 = y, Stroke = (Brush)FindResource("GridLineBrush"), StrokeThickness = 1 };
            GraphCanvas.Children.Add(line);

            double value = VoltageAxisRange - (2 * VoltageAxisRange * i / gridLineCount);
            var label = new TextBlock { Text = $"{value:0.0} V", Foreground = (Brush)FindResource("AxisTextBrush"), FontSize = 10 };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 1);
            GraphCanvas.Children.Add(label);
        }

        for (int i = 0; i < Channels.Count; i++)
        {
            var channel = Channels[i];
            var samples = _voltageHistory[channel.ChannelNumber].ToArray();
            if (samples.Length < 2)
            {
                continue;
            }

            var polyline = new Polyline { Stroke = ChannelColors[i % ChannelColors.Length], StrokeThickness = 1.5 };
            for (int s = 0; s < samples.Length; s++)
            {
                double x = width * s / (HistoryCapacity - 1);
                double y = (height / 2) - (samples[s] / VoltageAxisRange) * (height / 2);
                polyline.Points.Add(new Point(x, y));
            }

            GraphCanvas.Children.Add(polyline);
        }
    }

    private void RebuildMeasurementRows()
    {
        MeasurementRows.Clear();

        foreach (var channel in Channels)
        {
            var voltageSamples = _voltageHistory[channel.ChannelNumber].ToArray();
            var (vMean, vMin, vMax, vStd, vCount) = ComputeStats(voltageSamples);
            MeasurementRows.Add(new MeasurementRow
            {
                Channel = channel.ChannelName,
                Measurement = "Voltage",
                Value = channel.MeasuredVoltage,
                Mean = vMean,
                Minimum = vMin,
                Maximum = vMax,
                StatisticalRange = vMax - vMin,
                StdDeviation = vStd,
                Count = vCount,
            });

            // Current row: stats come from _currentHistory (this is the
            // channel's actual measured current, not voltage).
            var currentSamples = _currentHistory[channel.ChannelNumber].ToArray();
            var (iMean, iMin, iMax, iStd, iCount) = ComputeStats(currentSamples);
            MeasurementRows.Add(new MeasurementRow
            {
                Channel = channel.ChannelName,
                Measurement = "Current",
                Value = channel.MeasuredCurrent,
                Mean = iMean,
                Minimum = iMin,
                Maximum = iMax,
                StatisticalRange = iMax - iMin,
                StdDeviation = iStd,
                Count = iCount,
            });
        }
    }

    private static (double Mean, double Min, double Max, double StdDev, int Count) ComputeStats(double[] samples)
    {
        if (samples.Length == 0)
        {
            return (0, 0, 0, 0, 0);
        }

        double mean = samples.Average();
        double min = samples.Min();
        double max = samples.Max();
        double variance = samples.Select(v => (v - mean) * (v - mean)).Average();
        double stdDev = System.Math.Sqrt(variance);

        return (mean, min, max, stdDev, samples.Length);
    }

    private void SetVoltageTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)sender;
        var channel = (Channel)textBox.Tag;
        if (double.TryParse(textBox.Text, out double value))
        {
            channel.SetVoltage = value;
        }
        else
        {
            textBox.Text = channel.SetVoltage.ToString();
        }
    }

    private void ComplianceLimitTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)sender;
        var channel = (Channel)textBox.Tag;
        if (double.TryParse(textBox.Text, out double value))
        {
            channel.ComplianceCurrentLimit = value;
        }
        else
        {
            textBox.Text = channel.ComplianceCurrentLimit.ToString();
        }
    }
}
