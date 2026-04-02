# Canopus JD-1 Controller Mapper

This is a cross-platform software driver to make the old Canopus JD-1 video deck controller work with Windows and Linux.

<img width="1600" height="1600" alt="image" src="https://github.com/user-attachments/assets/e29d1c1f-1218-4cc5-9310-e828a70974c2" />

Just open and run the executable file and this window will show letting you map the keys to any keyboard keys you want.

<img width="441" height="596" alt="image" src="https://github.com/user-attachments/assets/f6ac9f13-0524-4090-a64a-d29f39919b65" />

When done mapping close the window it will stay running in the system tray here:

<img width="253" height="227" alt="image" src="https://github.com/user-attachments/assets/15c8c6e5-c8f5-4241-b57c-d72a1ba19742" />

## Building and Running

- Requires .NET 8.0 SDK or later
- Build with `dotnet build` in the `canopusMapApp/` directory
- Run the generated executable; app minimizes to system tray on close (Windows) or stays open (Linux)
- Configuration auto-saves to `canopus_settings_v2.ini` in app directory

## Linux Setup

On Linux, ensure `xdotool` is installed for keyboard simulation:

```bash
sudo apt-get install xdotool  # Debian/Ubuntu
# or equivalent for your distro
```

You may also need to configure udev rules for HID device access if running as non-root user.

## Features

- Maps HID inputs from Canopus JD-1 controller to keyboard keys
- Supports multiple connected devices simultaneously
- Cross-platform: Windows and Linux
- System tray integration
- Device diagnostics window for debugging HID communication
