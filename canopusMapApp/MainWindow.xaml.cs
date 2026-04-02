using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HidSharp;


using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;
using Grid = System.Windows.Controls.Grid;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using StackPanel = System.Windows.Controls.StackPanel;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CanopusMapApp
{
    public class DeviceContext
    {
        public string DevicePath { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = "n/a";
        public HidDevice? Device { get; set; }
        public HidStream? Stream { get; set; }
        public TabItem? Tab { get; set; }
        public StackPanel? MappingList { get; set; }
        
        public Dictionary<string, ComboBox> KeyCombos = new Dictionary<string, ComboBox>();
        public Dictionary<string, ComboBox> LabelCombos = new Dictionary<string, ComboBox>();
        public Dictionary<string, Border> UiRows = new Dictionary<string, Border>();
        public HashSet<string> PressedButtons = new HashSet<string>(StringComparer.Ordinal);
        public byte CurrentLedState = 0;
        public int LastBtn1 = 0, LastBtn2 = 0;
        public bool IsRefreshing = false;

        public DispatcherTimer JogTimerCW = new DispatcherTimer();
        public DispatcherTimer JogTimerCCW = new DispatcherTimer();
        
        public CancellationTokenSource ReadCts = new CancellationTokenSource();
    }

    public partial class MainWindow : Window
    {
        private const byte LedFeatureReportId = 0x03;
        private const byte DeckLedMask = 0x01;
        private const byte JogLedMask = 0x02;
        private const byte ShuttleLedMask = 0x04;
        private const int VendorId = 0x05E7;
        private const int ProductId = 0x0006;
        private string IniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "canopus_settings_v2.ini");

        private Dictionary<string, DeviceContext> _devices = new Dictionary<string, DeviceContext>();
        private readonly object _devicesLock = new object();
        private DeviceDiagnosticsWindow? _diagnosticsWindow;
        
        private List<string> _buttonPhysicalNames = new List<string> { 
            "NONE", "JOG BUTTON", "PLAY/PAUSE", "IN", "OUT", "REWIND", "FFWD", 
            "DECK/FILE", "CAP/UNDO", "ADD/DIV", "DOT 1", "DOT 2", "DOT 3", "DOT 4", "DOT 5",
            "SHUTTLE"
        };

        private CancellationTokenSource _mainCts = new CancellationTokenSource();
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _reallyExit = false;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
            StartHidListener();
        }

        private void SetupTrayIcon()
        { 
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trayIcon.ico");
            if (File.Exists(iconPath))
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application; 
            }
          
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "Canopus JD-1 Mapper";
            _notifyIcon.DoubleClick += (s, e) => { 
                this.Show(); 
                this.WindowState = WindowState.Normal;
                this.Activate();
            };
            
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Settings", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("Exit Completely", null, (s, e) => { _reallyExit = true; this.Close(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void InitializeDeviceUI(DeviceContext ctx)
        {
            var bitIds = new List<string> { "Jog_CW", "Jog_CCW" };
            for (int i = 0; i < 8; i++) bitIds.Add($"Button_A{i}");
            for (int i = 0; i < 6; i++) bitIds.Add($"Button_B{i}");

            var keyOptions = Enum.GetNames(typeof(Win32Key)).OrderBy(n => n).ToList();

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var mappingList = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            scrollViewer.Content = mappingList;
            ctx.MappingList = mappingList;

            var headerGrid = new Grid { Margin = new Thickness(10, 0, 10, 5) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.Children.Add(new TextBlock { Text = "Bit ID", Foreground = Brushes.Gray, FontSize = 10 });
            var labelHeader = new TextBlock { Text = "Physical Label", Foreground = Brushes.Gray, FontSize = 10 };
            Grid.SetColumn(labelHeader, 1); headerGrid.Children.Add(labelHeader);
            var keyHeader = new TextBlock { Text = "Keyboard Key", Foreground = Brushes.Gray, FontSize = 10 };
            Grid.SetColumn(keyHeader, 2); headerGrid.Children.Add(keyHeader);
            mappingList.Children.Add(headerGrid);

            foreach (var bitId in bitIds)
            {
                var rowBorder = new Border { 
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(3)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bitLabel = new TextBlock { Text = bitId, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(bitLabel, 0); grid.Children.Add(bitLabel);

                if (bitId.StartsWith("Jog")) {
                    var staticLabel = new TextBlock { 
                        Text = bitId == "Jog_CW" ? "JOG WHEEL CW" : "JOG WHEEL CCW",
                        Foreground = Brushes.SkyBlue, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center 
                    };
                    Grid.SetColumn(staticLabel, 1); grid.Children.Add(staticLabel);
                }
                else {
                    var labelCombo = new ComboBox { Margin = new Thickness(0, 0, 10, 0), Tag = bitId };
                    labelCombo.SelectionChanged += (s, e) => {
                        RefreshPhysicalLists(ctx);
                        UpdateLedsFromLabels(ctx);
                    };
                    Grid.SetColumn(labelCombo, 1); grid.Children.Add(labelCombo);
                    ctx.LabelCombos[bitId] = labelCombo;
                }

                var keyCombo = new ComboBox { ItemsSource = keyOptions, IsEditable = true, Tag = bitId };
                Grid.SetColumn(keyCombo, 2); grid.Children.Add(keyCombo);

                rowBorder.Child = grid;
                mappingList.Children.Add(rowBorder);
                ctx.UiRows[bitId] = rowBorder;
                ctx.KeyCombos[bitId] = keyCombo;
            }

            ctx.Tab = new TabItem { 
                Header = $"JD-1 ({ctx.SerialNumber})",
                Content = scrollViewer 
            };
            DeviceTabs.Items.Add(ctx.Tab);
            if (DeviceTabs.Items.Count == 1) ctx.Tab.IsSelected = true;

            SetupJogTimers(ctx);
            ForceInitialPopulate(ctx);
            LoadConfig(ctx);
            RefreshPhysicalLists(ctx);
        }

        private void SetupJogTimers(DeviceContext ctx)
        {
            ctx.JogTimerCW.Interval = TimeSpan.FromMilliseconds(200);
            ctx.JogTimerCW.Tick += (s, e) => { LightDown(ctx, "Jog_CW"); ctx.JogTimerCW.Stop(); };
            ctx.JogTimerCCW.Interval = TimeSpan.FromMilliseconds(200);
            ctx.JogTimerCCW.Tick += (s, e) => { LightDown(ctx, "Jog_CCW"); ctx.JogTimerCCW.Stop(); };
        }

        private void ForceInitialPopulate(DeviceContext ctx)
        {
            foreach (var combo in ctx.LabelCombos.Values)
            {
                combo.ItemsSource = _buttonPhysicalNames.ToList();
            }
        }

        private void RefreshPhysicalLists(DeviceContext ctx)
        {
            if (ctx.IsRefreshing) return;
            ctx.IsRefreshing = true;
            try {
                var usedNames = ctx.LabelCombos.Values.Select(c => c.SelectedItem?.ToString()).Where(s => s != null && s != "NONE").ToList();
                foreach (var kvp in ctx.LabelCombos) {
                    var combo = kvp.Value;
                    var current = combo.SelectedItem?.ToString() ?? "NONE";
                    var available = _buttonPhysicalNames.Where(name => name == "NONE" || name == current || !usedNames.Contains(name)).ToList();
                    
                    if (combo.ItemsSource == null || !((List<string>)combo.ItemsSource).SequenceEqual(available)) {
                        combo.ItemsSource = available; 
                        combo.SelectedItem = current;
                    }
                }
            } finally { ctx.IsRefreshing = false; }
        }

        private void StartHidListener()
        {
            Task.Run(() => {
                while (!_mainCts.Token.IsCancellationRequested) {
                    try {
                        var currentDevices = DeviceList.Local.GetHidDevices(VendorId, ProductId).ToList();
                        
                        lock (_devicesLock) {
                            // Detect removals
                            var removedPaths = _devices.Keys.Where(path => !currentDevices.Any(d => d.DevicePath == path)).ToList();
                            foreach (var path in removedPaths) {
                                var ctx = _devices[path];
                                ctx.ReadCts.Cancel();
                                Dispatcher.Invoke(() => {
                                    DeviceTabs.Items.Remove(ctx.Tab);
                                    _devices.Remove(path);
                                    UpdateStatusText();
                                });
                            }

                            // Detect additions
                            foreach (var device in currentDevices) {
                                if (!_devices.ContainsKey(device.DevicePath)) {
                                    var snapshot = HidDiagnostics.CreateSnapshot(device);
                                    var ctx = new DeviceContext {
                                        DevicePath = device.DevicePath,
                                        SerialNumber = snapshot.SerialNumber,
                                        Device = device
                                    };
                                    _devices[device.DevicePath] = ctx;
                                    Dispatcher.Invoke(() => {
                                        InitializeDeviceUI(ctx);
                                        UpdateStatusText();
                                    });
                                    StartDeviceReader(ctx);
                                }
                            }
                        }
                    } catch { }
                    Thread.Sleep(2000);
                }
            });
        }

        private void UpdateStatusText()
        {
            if (_devices.Count == 0) {
                StatusText.Text = "Status: Searching for JD-1...";
                StatusText.Foreground = Brushes.Orange;
            } else {
                StatusText.Text = $"Status: Connected ({_devices.Count} device(s))";
                StatusText.Foreground = Brushes.Lime;
            }
        }

        private void StartDeviceReader(DeviceContext ctx)
        {
            Task.Run(() => {
                while (!ctx.ReadCts.Token.IsCancellationRequested) {
                    try {
                        if (ctx.Device != null && ctx.Device.TryOpen(out HidStream stream)) {
                            ctx.Stream = stream;
                            Dispatcher.Invoke(() => UpdateDiagnosticsDevice(ctx.Device));
                            Dispatcher.Invoke(() => UpdateLedsFromLabels(ctx));
                            UpdateLeds(ctx, ctx.CurrentLedState);
                            using (stream) {
                                stream.ReadTimeout = Timeout.Infinite;
                                byte[] buffer = new byte[ctx.Device.GetMaxInputReportLength()];
                                while (!ctx.ReadCts.Token.IsCancellationRequested) {
                                    int bytesRead = stream.Read(buffer);
                                    if (bytesRead > 0) { try { HandleHidData(ctx, buffer, bytesRead); } catch { } }
                                }
                            }
                        }
                    } catch { }
                    ctx.Stream = null;
                    Thread.Sleep(2000);
                }
            }, ctx.ReadCts.Token);
        }

        private void HandleHidData(DeviceContext ctx, byte[] buffer, int bytesRead)
        {
            var report = buffer.Take(bytesRead).ToArray();
            Dispatcher.Invoke(() => {
                if (DeviceTabs.SelectedItem == ctx.Tab)
                    _diagnosticsWindow?.AppendInputReport(report);
            });

            byte reportId = buffer[0];
            if (reportId == 0x02) {
                sbyte jog = (sbyte)buffer[1];
                if (jog > 0) { TriggerKey(ctx, "Jog_CW"); LightUpJog(ctx, "Jog_CW"); }
                else if (jog < 0) { TriggerKey(ctx, "Jog_CCW"); LightUpJog(ctx, "Jog_CCW"); }
            }
            else if (reportId == 0x01) {
                CheckButtonStates(ctx, buffer[1], ctx.LastBtn1, "A");
                CheckButtonStates(ctx, buffer[2], ctx.LastBtn2, "B");
                ctx.LastBtn1 = buffer[1]; ctx.LastBtn2 = buffer[2];
            }
        }

        private void UpdateLeds(DeviceContext ctx, byte ledMask)
        {
            SendLedFeatureReport(ctx, ledMask);
        }

        private bool SendOutputReport(DeviceContext ctx, byte[] report)
        {
            if (ctx.Stream == null) return false;
            try
            {
                lock (ctx)
                {
                    ctx.Stream.Write(report);
                }
                Dispatcher.Invoke(() => {
                    if (DeviceTabs.SelectedItem == ctx.Tab)
                        _diagnosticsWindow?.AppendOutputReport(report, true, null);
                });
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    if (DeviceTabs.SelectedItem == ctx.Tab)
                        _diagnosticsWindow?.AppendOutputReport(report, false, ex.Message);
                });
                return false;
            }
        }

        private bool SendFeatureReport(DeviceContext ctx, byte[] report)
        {
            if (ctx.Stream == null) return false;
            try
            {
                lock (ctx)
                {
                    ctx.Stream.SetFeature(report);
                }
                Dispatcher.Invoke(() => {
                    if (DeviceTabs.SelectedItem == ctx.Tab)
                        _diagnosticsWindow?.AppendFeatureReport(report, true, null);
                });
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    if (DeviceTabs.SelectedItem == ctx.Tab)
                        _diagnosticsWindow?.AppendFeatureReport(report, false, ex.Message);
                });
                return false;
            }
        }

        private void SendLedFeatureReport(DeviceContext ctx, byte ledMask)
        {
            var report = NormalizeFeatureReport(ctx, new byte[] { LedFeatureReportId, ledMask });
            SendFeatureReport(ctx, report);
            if (DeviceTabs.SelectedItem == ctx.Tab) {
                var ledStateText = $"LED: 0x{ledMask:X2} (DECK={(ledMask & 0x01) != 0}, JOG={(ledMask & 0x02) != 0}, SHUTTLE={(ledMask & 0x04) != 0})";
                Dispatcher.Invoke(() => LedDebugText.Text = ledStateText);
            }
        }

        private byte[] NormalizeReport(DeviceContext ctx, byte[] report)
        {
            var outputLength = ctx.Device?.GetMaxOutputReportLength() ?? report.Length;
            if (outputLength <= 0) outputLength = report.Length;
            if (report.Length == outputLength) return report.ToArray();
            if (report.Length > outputLength) return report.Take(outputLength).ToArray();
            var padded = new byte[outputLength];
            Array.Copy(report, padded, report.Length);
            return padded;
        }

        private byte[] NormalizeFeatureReport(DeviceContext ctx, byte[] report)
        {
            var featureLength = ctx.Device?.GetMaxFeatureReportLength() ?? report.Length;
            if (featureLength <= 0) featureLength = report.Length;
            if (report.Length == featureLength) return report.ToArray();
            if (report.Length > featureLength) return report.Take(featureLength).ToArray();
            var padded = new byte[featureLength];
            Array.Copy(report, padded, report.Length);
            return padded;
        }

        private void CheckButtonStates(DeviceContext ctx, int current, int last, string group)
        {
            for (int i = 0; i < 8; i++) {
                int bit = 1 << i;
                string id = $"Button_{group}{i}";
                bool isPressed = (current & bit) != 0;
                bool wasPressed = (last & bit) == 0;
                
                Dispatcher.Invoke(() => { 
                    if (isPressed) {
                        LightUp(ctx, id); 
                        UpdateLedsForButton(ctx, id, true);
                    } else {
                        LightDown(ctx, id); 
                        UpdateLedsForButton(ctx, id, false);
                    } 
                });
                if (isPressed && wasPressed) TriggerKey(ctx, id);
            }
        }

        private void UpdateLedsForButton(DeviceContext ctx, string bitId, bool isPressed)
        {
            if (isPressed) ctx.PressedButtons.Add(bitId);
            else ctx.PressedButtons.Remove(bitId);
            RebuildLedStateFromPressedButtons(ctx);
        }

        private void UpdateLedsFromLabels(DeviceContext ctx)
        {
            RebuildLedStateFromPressedButtons(ctx);
        }

        private void RebuildLedStateFromPressedButtons(DeviceContext ctx)
        {
            byte ledState = DeckLedMask;
            foreach (var pressedButtonId in ctx.PressedButtons) {
                if (ctx.LabelCombos.TryGetValue(pressedButtonId, out var combo))
                    ledState |= GetLedMaskForLabel(combo.SelectedItem?.ToString());
            }
            if (ledState == ctx.CurrentLedState) return;
            ctx.CurrentLedState = ledState;
            UpdateLeds(ctx, ctx.CurrentLedState);
        }

        private static byte GetLedMaskForLabel(string? label)
        {
            return label switch {
                "DECK/FILE" => DeckLedMask,
                "JOG BUTTON" => JogLedMask,
                "SHUTTLE" => ShuttleLedMask,
                _ => 0
            };
        }

        private void LightUpJog(DeviceContext ctx, string id) {
            Dispatcher.Invoke(() => {
                LightUp(ctx, id);
                if (id == "Jog_CW") { ctx.JogTimerCW.Stop(); ctx.JogTimerCW.Start(); }
                else { ctx.JogTimerCCW.Stop(); ctx.JogTimerCCW.Start(); }
            });
        }

        private void LightUp(DeviceContext ctx, string id) { if (ctx.UiRows.ContainsKey(id)) ctx.UiRows[id].Background = new SolidColorBrush(Color.FromRgb(0, 100, 0)); }
        private void LightDown(DeviceContext ctx, string id) { if (ctx.UiRows.ContainsKey(id)) ctx.UiRows[id].Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)); }

        private void TriggerKey(DeviceContext ctx, string inputName) {
            Dispatcher.Invoke(() => {
                if (ctx.KeyCombos.TryGetValue(inputName, out var combo) && combo.SelectedItem != null) {
                    if (Enum.TryParse(combo.SelectedItem.ToString(), out Win32Key key) && key != Win32Key.NONE) 
                        NativeKeyboard.SendKey(key);
                }
            });
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e) {
            var sb = new StringBuilder();
            foreach (var ctx in _devices.Values) {
                sb.AppendLine($"[DEVICE|{ctx.SerialNumber}]");
                foreach (var bitId in ctx.KeyCombos.Keys) {
                    string label = bitId.StartsWith("Jog") ? (bitId == "Jog_CW" ? "JOG WHEEL CW" : "JOG WHEEL CCW") : (ctx.LabelCombos[bitId].SelectedItem?.ToString() ?? "NONE");
                    var key = ctx.KeyCombos[bitId].SelectedItem ?? "NONE";
                    sb.AppendLine($"{bitId}|{label}|{key}");
                }
            }
            File.WriteAllText(IniPath, sb.ToString());
            MessageBox.Show("Configuration Saved!");
        }

        private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceTabs.SelectedItem is not TabItem selectedTab) return;
            var ctx = _devices.Values.FirstOrDefault(d => d.Tab == selectedTab);
            if (ctx == null) return;

            if (_diagnosticsWindow == null) {
                _diagnosticsWindow = new DeviceDiagnosticsWindow(
                    report => SendManualReport(ctx, report), 
                    () => ProbeFeatureIds(ctx), 
                    ctx.Device == null ? null : HidDiagnostics.CreateSnapshot(ctx.Device));
                _diagnosticsWindow.Owner = this;
                _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
                _diagnosticsWindow.Show();
            } else {
                _diagnosticsWindow.Activate();
            }
            UpdateDiagnosticsDevice(ctx.Device);
        }

        private bool SendManualReport(DeviceContext ctx, byte[] report)
        {
            var outputSent = (ctx.Device?.GetMaxOutputReportLength() ?? 0) > 0 && SendOutputReport(ctx, NormalizeReport(ctx, report));
            var featureSent = (ctx.Device?.GetMaxFeatureReportLength() ?? 0) > 0 && SendFeatureReport(ctx, NormalizeFeatureReport(ctx, report));
            return outputSent || featureSent;
        }

        private string ProbeFeatureIds(DeviceContext ctx)
        {
            if ((ctx.Device?.GetMaxFeatureReportLength() ?? 0) != 2) return "Feature probing only runs for 2-byte feature reports.";
            var masks = new byte[] { 0x01, 0x02, 0x04, 0x07 };
            var successCount = 0;
            foreach (var reportId in Enumerable.Range(0, 256).Select(static i => (byte)i)) {
                foreach (var mask in masks) {
                    if (SendFeatureReport(ctx, new byte[] { reportId, mask })) successCount++;
                }
            }
            return successCount > 0 ? $"Probe complete. {successCount} feature report(s) succeeded." : "Probe complete. No 2-byte feature reports succeeded.";
        }

        private void UpdateDiagnosticsDevice(HidDevice? device)
        {
            _diagnosticsWindow?.SetDeviceSnapshot(device == null ? null : HidDiagnostics.CreateSnapshot(device));
        }

        private void LoadConfig(DeviceContext ctx) {
            if (!File.Exists(IniPath)) {
                ApplyDefaultHardwareMap(ctx);
                return;
            }
            var lines = File.ReadAllLines(IniPath);
            bool deviceFound = false;
            foreach (var line in lines) {
                if (line.StartsWith("[DEVICE|")) {
                    deviceFound = line.Contains(ctx.SerialNumber);
                    continue;
                }
                if (!deviceFound) continue;
                if (line.StartsWith("[")) break;

                var parts = line.Split('|');
                if (parts.Length == 3 && ctx.KeyCombos.ContainsKey(parts[0])) {
                    if (ctx.LabelCombos.ContainsKey(parts[0])) ctx.LabelCombos[parts[0]].SelectedItem = parts[1];
                    ctx.KeyCombos[parts[0]].SelectedItem = parts[2];
                }
            }
            if (!deviceFound) ApplyDefaultHardwareMap(ctx);
        }

        private void ApplyDefaultHardwareMap(DeviceContext ctx) {
            void Set(string id, string label, Win32Key key) {
                if (ctx.LabelCombos.ContainsKey(id)) ctx.LabelCombos[id].SelectedItem = label;
                ctx.KeyCombos[id].SelectedItem = key.ToString();
            }
            ctx.KeyCombos["Jog_CW"].SelectedItem = Win32Key.RIGHT.ToString();
            ctx.KeyCombos["Jog_CCW"].SelectedItem = Win32Key.LEFT.ToString();
            Set("Button_A0", "DOT 5", Win32Key.NONE);
            Set("Button_A1", "DOT 3", Win32Key.NONE);
            Set("Button_A2", "DOT 1", Win32Key.NONE);
            Set("Button_A3", "REWIND", Win32Key.J);
            Set("Button_A4", "CAP/UNDO", Win32Key.NONE);
            Set("Button_A5", "IN", Win32Key.I);
            Set("Button_A6", "ADD/DIV", Win32Key.NONE);
            Set("Button_A7", "DOT 4", Win32Key.NONE);
            Set("Button_B0", "DOT 2", Win32Key.NONE);
            Set("Button_B1", "DECK/FILE", Win32Key.NONE);
            Set("Button_B2", "FFWD", Win32Key.L);
            Set("Button_B3", "OUT", Win32Key.O);
            Set("Button_B4", "PLAY/PAUSE", Win32Key.SPACE);
            Set("Button_B5", "JOG BUTTON", Win32Key.NONE);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) 
        { 
            if (!_reallyExit) { e.Cancel = true; this.Hide(); } 
            else {
                lock (_devicesLock) {
                    foreach (var ctx in _devices.Values) {
                        ctx.ReadCts.Cancel();
                        try { UpdateLeds(ctx, 0); } catch { }
                    }
                }
                if (_notifyIcon != null) _notifyIcon.Dispose();
                _diagnosticsWindow?.Close();
                _mainCts.Cancel();
            }
            base.OnClosing(e); 
        }
    }

    public static class NativeKeyboard {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        public static void SendKey(Win32Key key) {
            keybd_event((byte)key, 0, 0, 0); keybd_event((byte)key, 0, 2, 0); 
        }
    }

    public enum Win32Key : byte {
        NONE = 0, SPACE = 0x20, LEFT = 0x25, UP = 0x26, RIGHT = 0x27, DOWN = 0x28, RETURN = 0x0D, BACK = 0x08, TAB = 0x09, ESCAPE = 0x1B,
        A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47, H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, 
        N = 0x4E, O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55, V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,
        D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34, D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,
        F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74, F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B,
        MEDIA_NEXT = 0xB0, MEDIA_PREV = 0xB1, MEDIA_STOP = 0xB2, MEDIA_PLAY = 0xB3, VOLUME_MUTE = 0xAD, VOLUME_DOWN = 0xAE, VOLUME_UP = 0xAF
    }
}
