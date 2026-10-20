# WiFiSurvey.Tests

Comprehensive unit test suite for the WiFiSurvey Wi-Fi site survey application.

## Overview

This test project uses **xUnit** as the testing framework with **FluentAssertions** for readable assertions and **Moq** for mocking dependencies. The tests focus on business logic and avoid P/Invoke Wi-Fi scanning operations.

## Test Coverage

### HeatmapGeneratorTests.cs
Tests the heatmap generation functionality:
- **SignalToColor conversion**: Validates signal strength to color mapping across the spectrum (-30 to -90 dBm)
- **IDW interpolation**: Tests Inverse Distance Weighting with known measurement points
- **Empty measurements**: Ensures graceful handling of empty data sets
- **Boundary conditions**: Tests edge cases like zero dimensions, out-of-range coordinates, and extreme signal values
- **Performance**: Validates generation with 50+ measurement points
- **Gaussian blur**: Tests smoothing algorithm
- **Preview generation**: Tests scaled preview rendering
- **Legend creation**: Validates legend image generation

**Test Count**: 20 tests

### MeasurementPointTests.cs
Tests the MeasurementPoint model:
- **FromMeasurement factory**: Validates conversion from WiFiMeasurement to MeasurementPoint
- **Signal quality classification**: Tests "Excellent/Good/Fair/Weak/Poor" thresholds
- **Color mapping**: Validates GetSignalColor() returns correct colors for signal strengths
- **QualityDescription**: Tests boundary conditions for quality ratings
- **Property management**: Tests getters/setters and default values
- **Unique IDs**: Ensures each instance gets a unique GUID
- **ToString formatting**: Validates string representation

**Test Count**: 24 tests

### SurveyProjectTests.cs
Tests the SurveyProject model and file operations:
- **JSON serialization**: Round-trip save/load with data preservation
- **Statistics calculation**: Tests GetStatistics() with various data sets
- **Measurement management**: AddMeasurement, RemoveMeasurement, ClearMeasurements
- **Filtering**: Tests TargetSSID filtering in statistics
- **Coverage classification**: Validates Excellent/Good/Fair/Weak/Poor categorization
- **File handling**: Tests save/load with valid/invalid files
- **Dirty tracking**: Ensures IsDirty flag updates correctly
- **Edge cases**: Empty projects, non-existent files, invalid JSON

**Test Count**: 18 tests

### FloorPlanTests.cs
Tests the FloorPlan model:
- **LoadFromFile**: Tests loading valid/invalid image paths
- **LoadFromBytes**: Tests loading from embedded image data
- **Coordinate conversion**: PixelsToMeters and MetersToPixels
- **Scale calculations**: TotalAreaSquareMeters, DimensionsMeters
- **Image formats**: PNG, JPEG, BMP support
- **Dispose cleanup**: Memory management and disposal
- **FromFile factory**: Static factory method validation
- **Multiple formats**: Tests various image file formats

**Test Count**: 22 tests

## Running the Tests

### Visual Studio
1. Open `WiFiSurvey.sln` in Visual Studio
2. Open **Test Explorer** (Test → Test Explorer)
3. Click **Run All** to execute all tests
4. View results in the Test Explorer window

### Command Line (Windows)
```powershell
# Navigate to solution directory
cd C:\Obsidian\tools\WiFiSurvey

# Restore packages
dotnet restore

# Build the solution
dotnet build

# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test file
dotnet test --filter "FullyQualifiedName~HeatmapGeneratorTests"

# Run with code coverage (requires coverlet)
dotnet test /p:CollectCoverage=true /p:CoverageReportFormat=opencover
```

### VS Code
1. Install the **.NET Core Test Explorer** extension
2. Open the WiFiSurvey folder in VS Code
3. Tests will appear in the Test Explorer sidebar
4. Click the play button to run tests

## Test Dependencies

- **xUnit** 2.6.2 - Testing framework
- **FluentAssertions** 6.12.0 - Readable assertions
- **Moq** 4.20.70 - Mocking framework (for future use)
- **Microsoft.NET.Test.Sdk** 17.8.0 - Test SDK
- **coverlet.collector** 6.0.0 - Code coverage

## Test Patterns

### Arrange-Act-Assert (AAA)
All tests follow the AAA pattern:
```csharp
[Fact]
public void Method_Scenario_ExpectedBehavior()
{
    // Arrange - Set up test data
    var generator = new HeatmapGenerator();
    var measurements = new List<MeasurementPoint> { ... };

    // Act - Execute the method under test
    var result = generator.GenerateHeatmap(200, 200, measurements);

    // Assert - Verify the outcome
    result.Should().NotBeNull();
    result.Width.Should().Be(200);
}
```

### Theory Tests
Parameterized tests use `[Theory]` with `[InlineData]`:
```csharp
[Theory]
[InlineData(-40, "Excellent")]
[InlineData(-55, "Good")]
[InlineData(-65, "Fair")]
public void QualityDescription_WithVariousSignalStrengths_ReturnsCorrectDescription(
    int signalStrength, string expectedDescription)
{
    var point = new MeasurementPoint { SignalStrength = signalStrength };
    point.QualityDescription.Should().Be(expectedDescription);
}
```

## Test Data

### Temporary Files
Tests that require file I/O create temporary directories:
- `SurveyProjectTests`: Creates temp directory for JSON files
- `FloorPlanTests`: Creates temp directory for test images

All temporary files are cleaned up in the test class destructor.

### Test Images
`FloorPlanTests` creates 800x600 PNG test images programmatically:
```csharp
private void CreateTestImage(string path, int width, int height)
{
    using var bitmap = new Bitmap(width, height);
    using var g = Graphics.FromImage(bitmap);
    g.Clear(Color.White);
    g.DrawRectangle(Pens.Black, 0, 0, width - 1, height - 1);
    bitmap.Save(path, ImageFormat.Png);
}
```

## Excluded from Testing

The following components are **NOT** tested due to platform-specific P/Invoke dependencies:
- `WiFiScanner.cs` - Requires Windows WLAN API (wlanapi.dll)
- `MainForm.cs` - Windows Forms UI
- `FloorPlanCanvas.cs` - Windows Forms custom control

These would require integration tests on a Windows environment with Wi-Fi hardware.

## Code Coverage Goals

| Component | Target Coverage | Current Status |
|-----------|----------------|----------------|
| HeatmapGenerator | 90% | Complete |
| MeasurementPoint | 95% | Complete |
| SurveyProject | 90% | Complete |
| FloorPlan | 90% | Complete |
| **Overall** | **90%+** | **84 tests** |

## Continuous Integration

To add these tests to CI/CD:

### GitHub Actions (.github/workflows/dotnet.yml)
```yaml
name: .NET Build and Test

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x
    - name: Restore dependencies
      run: dotnet restore tools/WiFiSurvey/WiFiSurvey.sln
    - name: Build
      run: dotnet build tools/WiFiSurvey/WiFiSurvey.sln --no-restore
    - name: Test
      run: dotnet test tools/WiFiSurvey/WiFiSurvey.sln --no-build --verbosity normal
```

## Contributing

When adding new features to WiFiSurvey:
1. Write tests first (TDD approach recommended)
2. Ensure tests follow naming convention: `Method_Scenario_ExpectedBehavior`
3. Maintain 90%+ code coverage
4. Use FluentAssertions for readable assertions
5. Add XML documentation to test methods explaining what they validate

## Troubleshooting

### Tests fail with "Image not found"
- Ensure FloorPlanTests creates temp directory correctly
- Check file permissions in temp folder

### "dotnet command not found"
- Install .NET 8.0 SDK from https://dotnet.microsoft.com/download

### Tests pass locally but fail in CI
- Ensure CI environment has .NET 8.0 SDK
- Verify Windows runtime for System.Drawing.Common

### OutOfMemoryException in HeatmapGeneratorTests
- Tests properly dispose bitmaps with `using` statements
- Check if temp directory cleanup is working

## License

Same as parent project (WiFiSurvey).
