using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace CanopusMapApp
{
    public partial class DeviceDiagnosticsWindow : Window
    {
        private readonly Func<byte[], bool> _sendReport;
        private readonly Func<string> _probeFeatureIds;

        public DeviceDiagnosticsWindow(Func<byte[], bool> sendReport, Func<string> probeFeatureIds, HidDeviceSnapshot? initialSnapshot)
        {
            InitializeComponent();
            _sendReport = sendReport;
            _probeFeatureIds = probeFeatureIds;
            SetDeviceSnapshot(initialSnapshot);
        }

        public void SetDeviceSnapshot(HidDeviceSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                ConnectionText.Text = "No JD-1 connected";
                ConnectionText.Foreground = Brushes.Orange;
                DeviceSummaryText.Text = "Waiting for the HID device to appear.";
                DeviceDetailsText.Text = string.Empty;
                return;
            }

            ConnectionText.Text = "JD-1 detected";
            ConnectionText.Foreground = Brushes.Lime;
            DeviceSummaryText.Text = $"VID 0x{snapshot.VendorId:X4} PID 0x{snapshot.ProductId:X4} | Input {snapshot.MaxInputReportLength} | Output {snapshot.MaxOutputReportLength} | Feature {snapshot.MaxFeatureReportLength}";

            var sb = new StringBuilder();
            sb.AppendLine($"Product: {snapshot.ProductName}");
            sb.AppendLine($"Manufacturer: {snapshot.Manufacturer}");
            sb.AppendLine($"Serial: {snapshot.SerialNumber}");
            sb.AppendLine($"Release BCD: 0x{snapshot.ReleaseNumberBcd:X4}");
            sb.AppendLine($"Max input report length: {snapshot.MaxInputReportLength}");
            sb.AppendLine($"Max output report length: {snapshot.MaxOutputReportLength}");
            sb.AppendLine($"Max feature report length: {snapshot.MaxFeatureReportLength}");
            sb.AppendLine($"Device path: {snapshot.DevicePath}");
            DeviceDetailsText.Text = sb.ToString();
        }

        public void AppendInputReport(byte[] report)
        {
            AppendLogLine(InputLogText, $"{DateTime.Now:HH:mm:ss.fff}  IN   {HidDiagnostics.FormatBytes(report)}");
        }

        public void AppendOutputReport(byte[] report, bool success, string? error)
        {
            var status = success ? "OK " : "ERR";
            var suffix = success ? string.Empty : $"  {error}";
            AppendLogLine(OutputLogText, $"{DateTime.Now:HH:mm:ss.fff}  OUT  [{status}]  {HidDiagnostics.FormatBytes(report)}{suffix}");
        }

        public void AppendFeatureReport(byte[] report, bool success, string? error)
        {
            var status = success ? "OK " : "ERR";
            var suffix = success ? string.Empty : $"  {error}";
            var line = $"{DateTime.Now:HH:mm:ss.fff}  FEAT [{status}]  {HidDiagnostics.FormatBytes(report)}{suffix}";
            AppendLogLine(FeatureLogText, line);
            if (success)
            {
                AppendLogLine(FeatureSuccessLogText, line);
            }
        }

        private void SendReport_Click(object sender, RoutedEventArgs e)
        {
            if (!HidDiagnostics.TryParseHexBytes(ManualReportText.Text ?? "", out var bytes, out var error))
            {
                SendStatusText.Text = error;
                SendStatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            var sent = _sendReport(bytes);
            SendStatusText.Text = sent
                ? $"Sent {bytes.Length} byte(s): {HidDiagnostics.FormatBytes(bytes)}"
                : "Send failed. Check the output log and connection state.";
            SendStatusText.Foreground = sent ? Brushes.Lime : Brushes.OrangeRed;
        }

        private void ClearInputLog_Click(object sender, RoutedEventArgs e)
        {
            InputLogText.Clear();
        }

        private void ClearOutputLog_Click(object sender, RoutedEventArgs e)
        {
            OutputLogText.Clear();
        }

        private void ClearFeatureLog_Click(object sender, RoutedEventArgs e)
        {
            FeatureLogText.Clear();
        }

        private void ClearFeatureSuccessLog_Click(object sender, RoutedEventArgs e)
        {
            FeatureSuccessLogText.Clear();
        }

        private void ProbeFeatureIds_Click(object sender, RoutedEventArgs e)
        {
            var result = _probeFeatureIds();
            SendStatusText.Text = result;
            SendStatusText.Foreground = Brushes.LightBlue;
        }

        private static void AppendLogLine(TextBox textBox, string line)
        {
            textBox.Text += line + Environment.NewLine;
            textBox.CaretIndex = textBox.Text.Length;
        }
    }
}
