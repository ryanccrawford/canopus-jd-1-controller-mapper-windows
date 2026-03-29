using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HidSharp;


using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;
using Grid = System.Windows.Controls.Grid;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CanopusMapApp
{
    public partial class MainWindow : Window
    {
        private const int VendorId = 0x05E7;
        private const int ProductId = 0x0006;
        private string IniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "canopus_settings_v2.ini");

        private Dictionary<string, ComboBox> _keyCombos = new Dictionary<string, ComboBox>();
        private Dictionary<string, ComboBox> _labelCombos = new Dictionary<string, ComboBox>();
        private Dictionary<string, Border> _uiRows = new Dictionary<string, Border>();
        
        private List<string> _buttonPhysicalNames = new List<string> { 
            "NONE", "JOG BUTTON", "PLAY/PAUSE", "IN", "OUT", "REWIND", "FFWD", 
            "DECK/FILE", "CAP/UNDO", "ADD/DIV", "DOT 1", "DOT 2", "DOT 3", "DOT 4", "DOT 5" 
        };

        private DispatcherTimer _jogTimerCW = new DispatcherTimer();
        private DispatcherTimer _jogTimerCCW = new DispatcherTimer();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private int _lastBtn1 = 0, _lastBtn2 = 0;
        private bool _isRefreshing = false;
        private bool _reallyExit = false;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
            SetupJogTimers();
            InitializeMappingUI();
            
            // 1. Give every dropdown the full list of options first
            ForceInitialPopulate();

            // 2. Apply the saved settings OR your custom hardware map
            if (File.Exists(IniPath)) LoadConfig(); 
            else ApplyYourHardwareMap();

            // 3. Filter the lists so used names are hidden
            RefreshPhysicalLists(); 
            StartHidListener();
        }

        private void SetupTrayIcon()
        { 
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trayIcon.ico"); // Match your filename
            if (File.Exists(iconPath))
            {
                System.Diagnostics.Debug.WriteLine("ICON FOUND SUCCESS!");
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                // Fallback to default if file is missing
                System.Diagnostics.Debug.WriteLine("ICON NOT FOUND! Using default.");
                MessageBox.Show($"Icon not found at: {iconPath}"); 
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application; 
            }
          
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "Canopus JD-1 Mapper";
            _notifyIcon.DoubleClick += (s, e) => { 
                this.Show(); 
                this.WindowState = WindowState.Normal;
                this.Activate(); // Bring to front
            };
            
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Settings", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("Exit Completely", null, (s, e) => { _reallyExit = true; this.Close(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void SetupJogTimers()
        {
            _jogTimerCW.Interval = TimeSpan.FromMilliseconds(200);
            _jogTimerCW.Tick += (s, e) => { LightDown("Jog_CW"); _jogTimerCW.Stop(); };
            _jogTimerCCW.Interval = TimeSpan.FromMilliseconds(200);
            _jogTimerCCW.Tick += (s, e) => { LightDown("Jog_CCW"); _jogTimerCCW.Stop(); };
        }

        private void InitializeMappingUI()
        {
            var bitIds = new List<string> { "Jog_CW", "Jog_CCW" };
            for (int i = 0; i < 8; i++) bitIds.Add($"Button_A{i}");
            for (int i = 0; i < 6; i++) bitIds.Add($"Button_B{i}");

            var keyOptions = Enum.GetNames(typeof(Win32Key)).OrderBy(n => n).ToList();

            foreach (var bitId in bitIds)
            {
                var rowBorder = new Border { 
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(3)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(80) });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(180) });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bitLabel = new TextBlock { Text = bitId, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Controls.Grid.SetColumn(bitLabel, 0); grid.Children.Add(bitLabel);

                if (bitId.StartsWith("Jog")) {
                    var staticLabel = new TextBlock { 
                        Text = bitId == "Jog_CW" ? "JOG WHEEL CW" : "JOG WHEEL CCW",
                        Foreground = Brushes.SkyBlue, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center 
                    };
                    System.Windows.Controls.Grid.SetColumn(staticLabel, 1); grid.Children.Add(staticLabel);
                }
                else {
                    var labelCombo = new ComboBox { Margin = new Thickness(0, 0, 10, 0), Tag = bitId };
                    labelCombo.SelectionChanged += (s, e) => RefreshPhysicalLists();
                    System.Windows.Controls.Grid.SetColumn(labelCombo, 1); grid.Children.Add(labelCombo);
                    _labelCombos[bitId] = labelCombo;
                }

                var keyCombo = new ComboBox { ItemsSource = keyOptions, IsEditable = true, Tag = bitId };
                System.Windows.Controls.Grid.SetColumn(keyCombo, 2); grid.Children.Add(keyCombo);

                rowBorder.Child = grid;
                MappingList.Children.Add(rowBorder);
                _uiRows[bitId] = rowBorder;
                _keyCombos[bitId] = keyCombo;
            }
        }

        private void ForceInitialPopulate()
        {
            foreach (var combo in _labelCombos.Values)
            {
                combo.ItemsSource = _buttonPhysicalNames.ToList();
            }
        }

        private void RefreshPhysicalLists()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            try {
                var usedNames = _labelCombos.Values.Select(c => c.SelectedItem?.ToString()).Where(s => s != null && s != "NONE").ToList();
                foreach (var kvp in _labelCombos) {
                    var combo = kvp.Value;
                    var current = combo.SelectedItem?.ToString() ?? "NONE";
                    var available = _buttonPhysicalNames.Where(name => name == "NONE" || name == current || !usedNames.Contains(name)).ToList();
                    
                    if (combo.ItemsSource == null || !((List<string>)combo.ItemsSource).SequenceEqual(available)) {
                        combo.ItemsSource = available; 
                        combo.SelectedItem = current;
                    }
                }
            } finally { _isRefreshing = false; }
        }

        private void StartHidListener()
        {
            Task.Run(() => {
                while (!_cts.Token.IsCancellationRequested) {
                    try {
                        var device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault();
                        if (device != null && device.TryOpen(out HidStream stream)) {
                            Dispatcher.Invoke(() => { StatusText.Text = "Status: Connected"; StatusText.Foreground = Brushes.Lime; });
                            using (stream) {
                                stream.ReadTimeout = Timeout.Infinite;
                                byte[] buffer = new byte[device.GetMaxInputReportLength()];
                                while (true) {
                                    int bytesRead = stream.Read(buffer);
                                    if (bytesRead > 0) { try { HandleHidData(buffer); } catch { } }
                                }
                            }
                        }
                    } catch { }
                    Dispatcher.Invoke(() => { StatusText.Text = "Status: Searching for JD-1..."; StatusText.Foreground = Brushes.Orange; });
                    Thread.Sleep(2000);
                }
            });
        }

        private void HandleHidData(byte[] buffer)
        {
            byte reportId = buffer[0];
            if (reportId == 0x02) {
                sbyte jog = (sbyte)buffer[1];
                if (jog > 0) { TriggerKey("Jog_CW"); LightUpJog("Jog_CW"); }
                else if (jog < 0) { TriggerKey("Jog_CCW"); LightUpJog("Jog_CCW"); }
            }
            else if (reportId == 0x01) {
                CheckButtonStates(buffer[1], _lastBtn1, "A");
                CheckButtonStates(buffer[2], _lastBtn2, "B");
                _lastBtn1 = buffer[1]; _lastBtn2 = buffer[2];
            }
        }

        private void CheckButtonStates(int current, int last, string group)
        {
            for (int i = 0; i < 8; i++) {
                int bit = 1 << i;
                string id = $"Button_{group}{i}";
                bool isPressed = (current & bit) != 0;
                bool wasPressed = (last & bit) == 0;
                
                Dispatcher.Invoke(() => { if (isPressed) LightUp(id); else LightDown(id); });
                if (isPressed && wasPressed) TriggerKey(id);
            }
        }

        private void LightUpJog(string id) {
            Dispatcher.Invoke(() => {
                LightUp(id);
                if (id == "Jog_CW") { _jogTimerCW.Stop(); _jogTimerCW.Start(); }
                else { _jogTimerCCW.Stop(); _jogTimerCCW.Start(); }
            });
        }

        private void LightUp(string id) { if (_uiRows.ContainsKey(id)) _uiRows[id].Background = new SolidColorBrush(Color.FromRgb(0, 100, 0)); }
        private void LightDown(string id) { if (_uiRows.ContainsKey(id)) _uiRows[id].Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)); }

        private void TriggerKey(string inputName) {
            Dispatcher.Invoke(() => {
                if (_keyCombos.TryGetValue(inputName, out var combo) && combo.SelectedItem != null) {
                    if (Enum.TryParse(combo.SelectedItem.ToString(), out Win32Key key) && key != Win32Key.NONE) 
                        NativeKeyboard.SendKey(key);
                }
            });
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e) {
            var sb = new StringBuilder();
            foreach (var bitId in _keyCombos.Keys) {
                string label = bitId.StartsWith("Jog") ? (bitId == "Jog_CW" ? "JOG WHEEL CW" : "JOG WHEEL CCW") : (_labelCombos[bitId].SelectedItem?.ToString() ?? "NONE");
                var key = _keyCombos[bitId].SelectedItem ?? "NONE";
                sb.AppendLine($"{bitId}|{label}|{key}");
            }
            File.WriteAllText(IniPath, sb.ToString());
            MessageBox.Show("Configuration Saved!");
        }

        private void LoadConfig() {
            if (!File.Exists(IniPath)) return;
            var lines = File.ReadAllLines(IniPath);
            foreach (var line in lines) {
                var parts = line.Split('|');
                if (parts.Length == 3 && _keyCombos.ContainsKey(parts[0])) {
                    if (_labelCombos.ContainsKey(parts[0])) _labelCombos[parts[0]].SelectedItem = parts[1];
                    _keyCombos[parts[0]].SelectedItem = parts[2];
                }
            }
        }

        private void ApplyYourHardwareMap() {
            void Set(string id, string label, Win32Key key) {
                if (_labelCombos.ContainsKey(id)) _labelCombos[id].SelectedItem = label;
                _keyCombos[id].SelectedItem = key.ToString();
            }

            _keyCombos["Jog_CW"].SelectedItem = Win32Key.RIGHT.ToString();
            _keyCombos["Jog_CCW"].SelectedItem = Win32Key.LEFT.ToString();

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
            else { if (_notifyIcon != null) _notifyIcon.Dispose(); _cts.Cancel(); }
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