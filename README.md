# RevEV - EV Engine Sound Simulator

A cross-platform .NET MAUI application that connects to OBD-II Bluetooth adapters to fetch real-time RPM/Speed data and generates dynamic ICE engine sounds for Electric Vehicles.

## Features

- **Real-time OBD-II Connection**: Connects to ELM327-compatible adapters via Bluetooth LE and Classic SPP
- **Dynamic Engine Sounds**: Sample-based audio with real-time pitch-shifting based on RPM
- **Smooth Interpolation**: 60Hz linear interpolation for seamless audio transitions
- **Cyberpunk UI**: Dark theme with neon accents and custom SkiaSharp gauges
- **Multiple Engine Profiles**: V8 Muscle, Sport Inline-6, and Futuristic sounds
- **Debug Terminal**: Raw OBD-II hex logging for troubleshooting

## Target Platforms

- **iOS** 14.2+
- **Android** API 24+ (Android 7.0)

## Project Structure

```
RevEVv2/
├── RevEV.sln                          # Solution file
├── README.md                          # This file
│
└── RevEV/
    ├── RevEV.csproj                   # Project file with NuGet dependencies
    ├── MauiProgram.cs                 # DI setup and service registration
    ├── App.xaml(.cs)                  # Application entry point
    ├── AppShell.xaml(.cs)             # Tab navigation shell
    ├── GlobalUsings.cs                # Global using statements
    │
    ├── Models/
    │   ├── VehicleData.cs             # RPM, Speed, interpolated values
    │   ├── EngineProfile.cs           # Sound profile definition
    │   └── OBDFrame.cs                # Raw OBD data frame
    │
    ├── Services/
    │   ├── Bluetooth/
    │   │   ├── IBluetoothService.cs   # Common interface
    │   │   ├── BleService.cs          # Bluetooth LE implementation
    │   │   ├── ClassicBluetoothService.cs  # SPP implementation (Android)
    │   │   ├── BluetoothManager.cs    # Connection orchestrator
    │   │   └── OBDProtocolHandler.cs  # ELM327 AT commands & PID parsing
    │   │
    │   ├── Audio/
    │   │   ├── IAudioEngine.cs        # Audio engine interface
    │   │   ├── AudioEngine.cs         # Real-time pitch-shifting engine
    │   │   ├── IEngineProfileManager.cs
    │   │   └── EngineProfileManager.cs # Profile management
    │   │
    │   ├── Interpolation/
    │   │   └── LerpInterpolator.cs    # Smooth RPM transitions
    │   │
    │   └── Settings/
    │       ├── IAppSettings.cs
    │       └── AppSettings.cs         # Persist preferences
    │
    ├── ViewModels/
    │   ├── BaseViewModel.cs           # Base class with busy state
    │   ├── DriveViewModel.cs          # Main HUD logic
    │   ├── EngineBayViewModel.cs      # Profile selection
    │   └── TerminalViewModel.cs       # Debug terminal
    │
    ├── Views/
    │   ├── DrivePage.xaml(.cs)        # Main HUD with Power Ring
    │   ├── EngineBayPage.xaml(.cs)    # Engine profile selection
    │   └── TerminalPage.xaml(.cs)     # Hex log debugging
    │
    ├── Controls/
    │   ├── PowerRing.cs               # SkiaSharp circular RPM gauge
    │   └── NeonButton.xaml(.cs)       # Styled button component
    │
    ├── Converters/
    │   └── ValueConverters.cs         # XAML value converters
    │
    ├── Resources/
    │   ├── AppIcon/
    │   │   ├── appicon.svg
    │   │   └── appiconfg.svg
    │   ├── Splash/
    │   │   └── splash.svg
    │   ├── Images/
    │   │   ├── drive_icon.svg
    │   │   ├── engine_icon.svg
    │   │   └── terminal_icon.svg
    │   ├── Fonts/
    │   │   └── font_readme.txt        # Instructions for Orbitron fonts
    │   ├── Raw/
    │   │   └── audio_readme.txt       # Instructions for audio samples
    │   └── Styles/
    │       ├── Colors.xaml            # Color palette
    │       ├── Styles.xaml            # Base control styles
    │       └── CyberpunkTheme.xaml    # Custom theme styles
    │
    └── Platforms/
        ├── Android/
        │   ├── AndroidManifest.xml    # Bluetooth permissions
        │   ├── MainActivity.cs
        │   └── MainApplication.cs
        └── iOS/
            ├── Info.plist             # BLE & audio background modes
            ├── AppDelegate.cs
            └── Program.cs
```

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Maui.Controls | 8.0.91 | MAUI framework |
| CommunityToolkit.Mvvm | 8.2.2 | MVVM source generators |
| Plugin.BLE | 3.1.0 | Bluetooth LE connectivity |
| Plugin.BluetoothClassic | 1.0.2 | Classic SPP (Android only) |
| SkiaSharp.Views.Maui.Controls | 2.88.8 | Custom graphics |
| Plugin.Maui.Audio | 3.0.0 | Audio playback |

## Setup Instructions

### 1. Prerequisites

- .NET 8.0 SDK
- Visual Studio 2022 or VS Code with MAUI extensions
- For iOS: macOS with Xcode
- For Android: Android SDK API 24+

### 2. Add Required Fonts

Download the **Orbitron** font family from [Google Fonts](https://fonts.google.com/specimen/Orbitron) and place in `RevEV/Resources/Fonts/`:

- `Orbitron-Regular.ttf`
- `Orbitron-Bold.ttf`
- `Orbitron-Black.ttf`

### 3. Add Audio Samples

Place loopable .wav files in `RevEV/Resources/Raw/`:

| File | Description | Base RPM |
|------|-------------|----------|
| `v8_muscle_loop.wav` | V8 muscle car rumble | 3000 |
| `sport_loop.wav` | Sport/inline-6 engine | 4000 |
| `futuristic_whine.wav` | Electric turbine whine | 5000 |

**Audio requirements:**
- Format: 44.1kHz, 16-bit, mono or stereo
- Must be seamlessly loopable
- Normalize to -3dB

### 4. Build and Run

```bash
# Restore packages
dotnet restore RevEV/RevEV.csproj

# Build for Android
dotnet build RevEV/RevEV.csproj -f net8.0-android

# Build for iOS (requires macOS)
dotnet build RevEV/RevEV.csproj -f net8.0-ios
```

## Technical Details

### OBD-II Communication

**Initialization Sequence:**
```
ATZ      → Reset adapter
ATE0     → Echo off
ATL0     → Linefeeds off
ATH0     → Headers off
ATSP0    → Auto-detect protocol
ATCAF1   → CAN formatting on
```

**Polling PIDs:**
- `010C` → RPM (Response: `41 0C XX YY`, RPM = ((XX×256)+YY)/4)
- `010D` → Speed (Response: `41 0D XX`, Speed = XX km/h)

### Audio Pitch Shifting

```csharp
// Base sample recorded at specific RPM
float baseSampleRPM = 3000f;
float pitchMultiplier = currentRPM / baseSampleRPM;
pitchMultiplier = Math.Clamp(pitchMultiplier, 0.3f, 3.0f);
```

**Platform implementations:**
- **Android**: `AudioTrack` with `PlaybackParams.SetPitch()`
- **iOS**: `AVAudioEngine` with `AVAudioUnitTimePitch`

### Linear Interpolation (60Hz)

```csharp
float smoothingFactor = 0.15f;
currentRPM = currentRPM + (targetRPM - currentRPM) * smoothingFactor;
```

## Color Palette (Cyberpunk Theme)

| Name | Hex | Usage |
|------|-----|-------|
| Void Black | `#000000` | Background |
| Deep Space | `#0A0A0F` | Card backgrounds |
| Neon Cyan | `#00FFFF` | Primary accent, RPM gauge |
| Plasma Pink | `#FF00FF` | Secondary accent, speed |
| Grid Gray | `#1A1A2E` | Borders, dividers |
| Terminal Green | `#00FF41` | TX logs |
| Warning Red | `#FF3366` | Errors, RX logs |

## App Screens

### 1. Drive Page (Main HUD)
- Circular Power Ring gauge showing RPM percentage
- Large digital RPM display in center
- Speed readout below with km/h units
- Connection status indicator
- Scan/Connect/Disconnect controls
- Audio toggle button

### 2. Engine Bay Page
- Grid of engine profile cards
- Profile name, description, and tags
- Preview button to test sounds
- Visual selection indicator

### 3. Terminal Page
- Scrolling hex log with timestamps
- Color-coded TX (green) and RX (cyan) messages
- Quick command buttons (ATZ, ATI, ATRV, PIDs)
- Manual command input
- Pause/Resume and Clear controls

## Platform Permissions

### Android (AndroidManifest.xml)
```xml
<uses-permission android:name="android.permission.BLUETOOTH" />
<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" />
<uses-permission android:name="android.permission.BLUETOOTH_SCAN" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-feature android:name="android.hardware.bluetooth_le" />
```

### iOS (Info.plist)
```xml
<key>NSBluetoothAlwaysUsageDescription</key>
<string>RevEV needs Bluetooth to connect to your OBD-II adapter</string>
<key>UIBackgroundModes</key>
<array>
    <string>bluetooth-central</string>
    <string>audio</string>
</array>
```

## Features Implemented

- [x] .NET MAUI project structure
- [x] Bluetooth LE service (Plugin.BLE)
- [x] Classic Bluetooth SPP service (Android)
- [x] BluetoothManager with auto-reconnect
- [x] OBD-II protocol handler (ELM327)
- [x] RPM and Speed PID parsing
- [x] Audio engine with pitch shifting
- [x] Engine profile management
- [x] Linear interpolation (60Hz)
- [x] SkiaSharp Power Ring control
- [x] Cyberpunk theme and styles
- [x] Drive Page with gauges
- [x] Engine Bay Page with profiles
- [x] Terminal Page with hex logging
- [x] Platform permissions configured
- [x] App icons and splash screen

## Future Enhancements

- [ ] Haptic feedback for gear changes
- [ ] Additional OBD-II PIDs (throttle, coolant temp)
- [ ] Custom engine profile creation
- [ ] Sound recording and import
- [ ] Landscape mode support
- [ ] CarPlay / Android Auto integration
- [ ] Cloud profile sharing

## Troubleshooting

### Bluetooth Connection Issues
1. Ensure Bluetooth is enabled on device
2. Check that OBD adapter is powered (plugged into vehicle)
3. For Android < 12, location permission is required for BLE scanning
4. Try Classic Bluetooth if BLE doesn't work with your adapter

### No Audio Output
1. Verify audio sample files are in `Resources/Raw/`
2. Check device volume and mute settings
3. On iOS, ensure audio background mode is enabled

### OBD Communication Errors
1. Use Terminal page to send `ATZ` and verify response
2. Check adapter compatibility (ELM327 v1.5+ recommended)
3. Ensure vehicle ignition is ON

## License

This project is provided as-is for educational purposes.

## Acknowledgments

- [Plugin.BLE](https://github.com/dotnet-bluetooth-le/dotnet-bluetooth-le) for Bluetooth LE
- [SkiaSharp](https://github.com/mono/SkiaSharp) for 2D graphics
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM
- [Orbitron Font](https://fonts.google.com/specimen/Orbitron) by Matt McInerney
