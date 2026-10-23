# Wi-Fi Survey Tool

A professional Wi-Fi site survey and heatmap generation tool for IT professionals. Upload floor plans, click to measure Wi-Fi signal strength at various locations, and generate color-coded heatmap visualizations.

## Features

- **Floor Plan Support**: Load PNG, JPG, BMP, or GIF images of floor plans
- **Click-to-Measure**: Click anywhere on the floor plan to take a Wi-Fi measurement
- **Real-time Heatmap**: Automatic heatmap generation using IDW interpolation
- **Signal Metrics**: RSSI (dBm), link quality, channel, frequency, and band
- **Project Files**: Save and load survey projects (.wifisurvey)
- **Export Options**: CSV data export, heatmap image export, HTML reports

## Requirements

- Windows 10/11 (64-bit)
- .NET 8 Runtime
- Wi-Fi adapter with support for Native Wi-Fi API

## Building

```bash
# Navigate to WiFiSurvey directory
cd WiFiSurvey

# Restore packages
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run
```

## Usage

### Quick Start

1. **Load Floor Plan**: File > Load Floor Plan, or click "Load Image" in toolbar
2. **Take Measurements**: Click on locations in the floor plan to measure Wi-Fi
3. **View Heatmap**: After 3+ measurements, a heatmap automatically appears
4. **Export Results**: Export to CSV, heatmap image, or HTML report

### Controls

| Action | Control |
|--------|---------|
| Take Measurement | Left-click on floor plan |
| Select Point | Left-click on measurement marker |
| Pan View | Right-click + drag |
| Zoom | Mouse wheel |
| Reset View | View > Reset View |

### Signal Quality Legend

| Color | Signal Strength | Quality |
|-------|-----------------|---------|
| Green | -30 to -50 dBm | Excellent |
| Yellow-Green | -50 to -60 dBm | Good |
| Yellow | -60 to -70 dBm | Fair |
| Orange | -70 to -80 dBm | Weak |
| Red | Below -80 dBm | Poor |

## Project Structure

```
WiFiSurvey/
├── WiFiSurvey.sln              # Solution file
├── WiFiSurvey.csproj           # .NET 8 WinForms project
├── Program.cs                  # Entry point
├── MainForm.cs                 # Main application form
├── Models/
│   ├── MeasurementPoint.cs     # Wi-Fi measurement data
│   ├── FloorPlan.cs            # Floor plan image and scale
│   └── SurveyProject.cs        # Project container
├── Services/
│   ├── WiFiScanner.cs          # Native Wi-Fi API wrapper
│   └── HeatmapGenerator.cs     # IDW interpolation heatmap
├── Controls/
│   └── FloorPlanCanvas.cs      # Custom floor plan control
└── README.md                   # This file
```

## Technical Details

### Wi-Fi Scanning

Uses the Windows Native Wi-Fi API (wlanapi.dll) through P/Invoke to retrieve:
- RSSI (Received Signal Strength Indicator) in dBm
- Link quality percentage
- Channel and frequency
- BSSID (access point MAC address)
- Maximum data rate

### Heatmap Generation

- **Algorithm**: Inverse Distance Weighting (IDW) interpolation
- **Smoothing**: Gaussian blur for visual clarity
- **Color Gradient**: Red (weak) → Yellow → Green (strong)
- **Performance**: Preview mode with scaling for real-time updates

### Project File Format

Projects are saved as JSON files with embedded floor plan images:

```json
{
  "version": "1.0",
  "name": "Office Survey",
  "floorPlan": { ... },
  "measurementPoints": [ ... ],
  "embeddedFloorPlanImage": "base64..."
}
```

## Tips for Effective Surveys

1. **Grid Pattern**: Take measurements in a regular grid for uniform coverage
2. **Focus Areas**: Add extra measurements near problem areas
3. **Consistent Height**: Hold device at consistent height (desk level)
4. **Multiple Passes**: Survey at different times to account for interference
5. **Document Notes**: Use the note field to mark special conditions

## Export Formats

### CSV Export
Includes all measurement data with columns:
- Timestamp, X, Y, SSID, BSSID, SignalStrength, LinkQuality, Channel, Band, Frequency, MaxRate, Note

### Heatmap Image
- PNG or JPG with floor plan and heatmap overlay
- Full resolution of original floor plan

### HTML Report
- Summary statistics
- Coverage distribution table
- All measurement points
- Ready for printing or sharing

## Troubleshooting

### "Wi-Fi Scanner Error" on startup
- Ensure Wi-Fi adapter is enabled
- Check that Windows WLAN AutoConfig service is running
- Run as Administrator if issues persist

### Heatmap not appearing
- Minimum 3 measurement points required
- Check View > Show Heatmap is enabled

### Measurements show -100 dBm
- No Wi-Fi connection detected
- Move closer to an access point
- Check Wi-Fi adapter is functioning

## Project Origin and Attribution

Wi-Fi Survey is derived from [WifiSurvey by CosmicBytez](https://github.com/CosmicBytez/WifiSurvey). The code was initially developed in a fork of that repository. This independently published repository retains the original project's Git ancestry and the early development included through the merge of PR #2 on October 20, 2026.

- **Original project and author:** [CosmicBytez/WifiSurvey](https://github.com/CosmicBytez/WifiSurvey), CosmicBytez.
- **Original MIT notice:** `Copyright (c) 2025 CosmicBytez` (preserved in the unmodified [LICENSE](LICENSE)).
- **Independent 2026 modifications:** R083rt0kun, identified in the retained Git history and project metadata.
- **Public functional baseline:** PR #2 merge commit [`94a408119a9dbddb2b24008eebe0a816dceb5126`](https://github.com/R083rt0kun/WiFiSurvey/commit/94a408119a9dbddb2b24008eebe0a816dceb5126).
- **Chronological record:** [Public Development History](docs/PROJECT_HISTORY.md), ending at PR #2.

This is an independently published derivative, not an official release by or endorsement from the original author. Historical PR numbers identify development milestones; original PR discussion pages are not presumed to survive repository replacement.
## License

This project is based on software originally published by CosmicBytez under the MIT License.

The original copyright and license notices are preserved in the [LICENSE](LICENSE) file.

The original author's rights are retained. Independently authored modifications may have their own copyright protection, subject to applicable law and the terms of the original license.
