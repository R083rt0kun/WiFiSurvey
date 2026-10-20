# WiFi Survey Tool

A professional WiFi site survey and heatmap generation tool for IT professionals. Upload floor plans, click to measure WiFi signal strength at various locations, and generate color-coded heatmap visualizations.

## Features

- **Floor Plan Support**: Load PNG, JPG, BMP, or GIF images of floor plans
- **Click-to-Measure**: Click anywhere on the floor plan to take a WiFi measurement
- **Real-time Heatmap**: Automatic heatmap generation using IDW interpolation
- **Signal Metrics**: RSSI (dBm), link quality, channel, frequency, and band
- **Project Files**: Save and load survey projects (.wifisurvey)
- **Export Options**: CSV data export, heatmap image export, HTML reports

## Requirements

- Windows 10/11 (64-bit)
- .NET 8 Runtime
- WiFi adapter with support for Native WiFi API

## Building

```bash
# Navigate to WifiSurvey directory
cd tools/WifiSurvey

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
2. **Take Measurements**: Click on locations in the floor plan to measure WiFi
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
WifiSurvey/
├── WifiSurvey.sln              # Solution file
├── WifiSurvey.csproj           # .NET 8 WinForms project
├── Program.cs                  # Entry point
├── MainForm.cs                 # Main application form
├── Models/
│   ├── MeasurementPoint.cs     # WiFi measurement data
│   ├── FloorPlan.cs            # Floor plan image and scale
│   └── SurveyProject.cs        # Project container
├── Services/
│   ├── WifiScanner.cs          # Native WiFi API wrapper
│   └── HeatmapGenerator.cs     # IDW interpolation heatmap
├── Controls/
│   └── FloorPlanCanvas.cs      # Custom floor plan control
└── README.md                   # This file
```

## Technical Details

### WiFi Scanning

Uses the Windows Native WiFi API (wlanapi.dll) through P/Invoke to retrieve:
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

### "WiFi Scanner Error" on startup
- Ensure WiFi adapter is enabled
- Check that Windows WLAN AutoConfig service is running
- Run as Administrator if issues persist

### Heatmap not appearing
- Minimum 3 measurement points required
- Check View > Show Heatmap is enabled

### Measurements show -100 dBm
- No WiFi connection detected
- Move closer to an access point
- Check WiFi adapter is functioning

## Project Origin and Attribution

This repository is an independently maintained fork of [WifiSurvey](https://github.com/CosmicBytez/WifiSurvey), originally developed by CosmicBytez.

- **Original project:** [CosmicBytez/WifiSurvey](https://github.com/CosmicBytez/WifiSurvey)
- **Original copyright:** Copyright (c) 2025 CosmicBytez
- **Independent development and modifications:** R083rt0kun, 2026–present

This fork preserves the original Git commit history, copyright notices, and MIT License.

Subsequent modifications and improvements are independently maintained by R083rt0kun. The repository's commit history provides a record of contributions and changes made to the project.

This fork is independently maintained and is not affiliated with or endorsed by the original developer.

## License

This project is based on software originally published by CosmicBytez under the MIT License.

The original copyright and license notices are preserved in the [LICENSE](LICENSE) file.

The original author's rights are retained. Independently authored modifications may have their own copyright protection, subject to applicable law and the terms of the original license.
## See Also

- [WinPing](../WinPing/) - Network ping utility
- [Tools Library README](../README.md) - Overview of all IT tools
