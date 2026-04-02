# AGENTS.md - Canopus JD-1 Controller Mapper

## Architecture Overview
This is an Avalonia application that maps HID inputs from Canopus JD-1 video deck controllers to keyboard keys on Windows and Linux. It uses the HidSharp library for low-level HID communication and supports multiple connected devices simultaneously.

Key components:
- **MainWindow**: Core UI with tabbed interface for device configuration, tray icon management, and HID event handling
- **DeviceContext**: Per-device state container holding UI elements, HID streams, and mapping data
- **HidDiagnostics**: Utility class for device snapshots and hex byte parsing
- **NativeKeyboard**: Cross-platform wrapper for sending keyboard events (keybd_event on Windows, xdotool on Linux)

Data flows from HID device reports → button/jog parsing → key mapping lookup → keyboard simulation.

## Critical Workflows

### Building and Running
- Build with `dotnet build` in the `canopusMapApp/` directory
- Run the generated exe; app minimizes to system tray on close
- Configuration auto-saves to `canopus_settings_v2.ini` in app directory

### Debugging HID Communication
- Open diagnostics window from "Open Diagnostics" button
- View live input/output/feature reports in hex format
- Send manual reports or probe feature IDs for LED control
- LED states: 0x01=DECK, 0x02=JOG, 0x04=SHUTTLE (combinable)

### Device Mapping
- Bit IDs: Button_A0-A7, Button_B0-B5, Jog_CW/Jog_CCW
- Physical labels: "PLAY/PAUSE", "REWIND", "FFWD", etc. (unique per device)
- Default mapping in `ApplyDefaultHardwareMap()` method
- Config format: `[DEVICE|serial]\nbitId|label|key` per line

## Project Conventions

### HID Report Handling
- Report ID 0x01: Button states (bytes 1-2 contain bitmasks)
- Report ID 0x02: Jog wheel (byte 1 as signed byte, positive=CW)
- Feature reports for LED control (ID 0x03, byte 1 = mask)
- Normalize reports to device max lengths before sending

### UI State Management
- Device tabs created dynamically on connect/disconnect
- ComboBox items refreshed to prevent duplicate physical label assignments
- Jog wheel lighting uses 200ms timers for visual feedback
- Pressed buttons tracked in HashSet for LED state calculation

### Error Handling
- HID operations wrapped in try-catch; failures logged to diagnostics
- Device reconnection attempted every 2 seconds on failure
- UI updates marshaled to dispatcher thread

## Integration Points

### HID Device
- Vendor ID: 0x05E7, Product ID: 0x0006
- Polled every 2 seconds for connect/disconnect events
- Streams opened with infinite read timeout

### Keyboard Simulation
- Windows: Uses `keybd_event` P/Invoke
- Linux: Uses `xdotool` command-line tool
- Win32Key enum maps common keys (A-Z, F1-F12, media keys, etc.)

### System Tray
- Avalonia TrayIcon for cross-platform tray support
- Context menu: "Open Settings", "Exit Completely"

### Dependencies
- HidSharp 2.6.4: Core HID communication
- Avalonia 11.0.10: Cross-platform UI framework
- .NET 8.0: Runtime framework
