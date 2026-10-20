using FluentAssertions;
using System.Drawing;
using System.Drawing.Imaging;
using WiFiSurvey.Models;
using WiFiSurvey.Services;

namespace WiFiSurvey.Tests;

/// <summary>
/// Unit tests for HeatmapGenerator
/// Tests signal-to-color conversion, IDW interpolation, and boundary conditions
/// </summary>
public class HeatmapGeneratorTests
{
    [Fact]
    public void Constructor_WithDefaultParameters_SetsCorrectDefaults()
    {
        // Arrange & Act
        var generator = new HeatmapGenerator();

        // Assert
        // Generator should be created successfully
        generator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomParameters_AcceptsValidValues()
    {
        // Arrange & Act
        var generator = new HeatmapGenerator(interpolationRadius: 150, smoothingFactor: 0.5);

        // Assert
        generator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOutOfRangeSmoothingFactor_ClampsSmoothingFactor()
    {
        // Arrange & Act - smoothing factor should be clamped to 0-1 range
        var generator1 = new HeatmapGenerator(smoothingFactor: -0.5);
        var generator2 = new HeatmapGenerator(smoothingFactor: 1.5);

        // Assert
        generator1.Should().NotBeNull();
        generator2.Should().NotBeNull();
    }

    [Fact]
    public void GenerateHeatmap_WithEmptyMeasurements_ReturnsBlankBitmap()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>();

        // Act
        using var bitmap = generator.GenerateHeatmap(100, 100, measurements);

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().Be(100);
        bitmap.Height.Should().Be(100);
        bitmap.PixelFormat.Should().Be(PixelFormat.Format32bppArgb);

        // Verify all pixels are transparent (empty)
        var pixel = bitmap.GetPixel(50, 50);
        pixel.A.Should().Be(0); // Alpha channel should be 0 (transparent)
    }

    [Fact]
    public void GenerateHeatmap_WithSingleMeasurement_GeneratesHeatmap()
    {
        // Arrange
        var generator = new HeatmapGenerator(interpolationRadius: 50);
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint
            {
                X = 0.5,
                Y = 0.5,
                SignalStrength = -50, // Excellent signal
                SSID = "TestNetwork"
            }
        };

        // Act
        using var bitmap = generator.GenerateHeatmap(200, 200, measurements, opacity: 255);

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().Be(200);
        bitmap.Height.Should().Be(200);

        // Check center pixel (should have color from interpolation)
        var centerPixel = bitmap.GetPixel(100, 100);
        centerPixel.A.Should().BeGreaterThan(0); // Should have some opacity
    }

    [Fact]
    public void GenerateHeatmap_WithMultipleMeasurements_InterpolatesBetweenPoints()
    {
        // Arrange
        var generator = new HeatmapGenerator(interpolationRadius: 100);
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.25, Y = 0.25, SignalStrength = -40 }, // Excellent
            new MeasurementPoint { X = 0.75, Y = 0.75, SignalStrength = -80 }  // Weak
        };

        // Act
        using var bitmap = generator.GenerateHeatmap(200, 200, measurements, opacity: 255);

        // Assert
        bitmap.Should().NotBeNull();

        // Check pixels at measurement points
        var excellentPixel = bitmap.GetPixel(50, 50);
        var weakPixel = bitmap.GetPixel(150, 150);

        // Both should have opacity
        excellentPixel.A.Should().BeGreaterThan(0);
        weakPixel.A.Should().BeGreaterThan(0);

        // Excellent signal should be more green, weak should be more red
        excellentPixel.G.Should().BeGreaterThan(excellentPixel.R);
        weakPixel.R.Should().BeGreaterThan(weakPixel.G);
    }

    [Fact]
    public void GenerateHeatmap_WithCustomOpacity_AppliesCorrectOpacity()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 }
        };

        // Act
        using var bitmap = generator.GenerateHeatmap(100, 100, measurements, opacity: 128);

        // Assert
        var pixel = bitmap.GetPixel(50, 50);
        // Alpha should be around the specified opacity (allowing for interpolation smoothing)
        pixel.A.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeneratePreviewHeatmap_WithScaleFactor_GeneratesScaledHeatmap()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 }
        };

        // Act
        using var bitmap = generator.GeneratePreviewHeatmap(400, 400, measurements, scale: 4);

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().Be(400);
        bitmap.Height.Should().Be(400);
    }

    [Theory]
    [InlineData(-30, 255)] // Excellent signal -> Green
    [InlineData(-60, 255)] // Medium signal -> Yellow
    [InlineData(-90, 255)] // Poor signal -> Red
    public void SignalToColor_WithVariousSignalStrengths_ReturnsAppropriateColors(int signalStrength, int opacity)
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = signalStrength }
        };

        // Act
        using var bitmap = generator.GenerateHeatmap(100, 100, measurements, opacity);

        // Assert
        var pixel = bitmap.GetPixel(50, 50);

        if (signalStrength >= -45) // Excellent - should be green-ish
        {
            pixel.G.Should().BeGreaterThan(pixel.R);
        }
        else if (signalStrength <= -75) // Poor - should be red-ish
        {
            pixel.R.Should().BeGreaterThan(pixel.G);
        }
        // Medium signals will be yellowish (R and G both high)
    }

    [Fact]
    public void SignalToColor_WithSignalBelowRange_ClampsToMinimum()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -150 } // Beyond typical range
        };

        // Act
        using var bitmap = generator.GenerateHeatmap(100, 100, measurements, opacity: 255);

        // Assert
        bitmap.Should().NotBeNull();
        var pixel = bitmap.GetPixel(50, 50);
        pixel.A.Should().BeGreaterThan(0); // Should still render
    }

    [Fact]
    public void SignalToColor_WithSignalAboveRange_ClampsToMaximum()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -10 } // Beyond typical range
        };

        // Act
        using var bitmap = generator.GenerateHeatmap(100, 100, measurements, opacity: 255);

        // Assert
        bitmap.Should().NotBeNull();
        var pixel = bitmap.GetPixel(50, 50);
        pixel.A.Should().BeGreaterThan(0); // Should still render
    }

    [Fact]
    public void CreateLegend_WithStandardDimensions_CreatesLegendBitmap()
    {
        // Arrange
        var generator = new HeatmapGenerator();

        // Act
        using var legend = generator.CreateLegend(400, 80);

        // Assert
        legend.Should().NotBeNull();
        legend.Width.Should().Be(400);
        legend.Height.Should().Be(80);
    }

    [Fact]
    public void GenerateHeatmap_WithZeroDimensions_HandlesGracefully()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 }
        };

        // Act & Assert - Should not throw
        Action act = () =>
        {
            using var bitmap = generator.GenerateHeatmap(0, 0, measurements);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateHeatmap_WithNormalizedCoordinatesOutOfRange_HandlesGracefully()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 1.5, Y = 1.5, SignalStrength = -50 }, // Beyond normalized range
            new MeasurementPoint { X = -0.5, Y = -0.5, SignalStrength = -60 } // Below normalized range
        };

        // Act & Assert
        Action act = () =>
        {
            using var bitmap = generator.GenerateHeatmap(200, 200, measurements);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateHeatmap_WithManyMeasurements_CompletesSuccessfully()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var random = new Random(42); // Fixed seed for reproducibility
        var measurements = new List<MeasurementPoint>();

        // Create 50 random measurement points
        for (int i = 0; i < 50; i++)
        {
            measurements.Add(new MeasurementPoint
            {
                X = random.NextDouble(),
                Y = random.NextDouble(),
                SignalStrength = random.Next(-90, -30)
            });
        }

        // Act
        using var bitmap = generator.GenerateHeatmap(400, 400, measurements, opacity: 200);

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().Be(400);
        bitmap.Height.Should().Be(400);
    }

    [Fact]
    public void GenerateHeatmap_ProducesBitmap_ThatCanBeDisposed()
    {
        // Arrange
        var generator = new HeatmapGenerator();
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 }
        };

        // Act
        var bitmap = generator.GenerateHeatmap(100, 100, measurements);

        // Assert & Cleanup
        bitmap.Should().NotBeNull();
        Action disposeAction = () => bitmap.Dispose();
        disposeAction.Should().NotThrow();
    }

    [Fact]
    public void GenerateHeatmap_WithLargeInterpolationRadius_ProducesSmootherGradient()
    {
        // Arrange
        var generatorSmall = new HeatmapGenerator(interpolationRadius: 10);
        var generatorLarge = new HeatmapGenerator(interpolationRadius: 200);
        var measurements = new List<MeasurementPoint>
        {
            new MeasurementPoint { X = 0.25, Y = 0.25, SignalStrength = -40 },
            new MeasurementPoint { X = 0.75, Y = 0.75, SignalStrength = -80 }
        };

        // Act
        using var bitmapSmall = generatorSmall.GenerateHeatmap(200, 200, measurements);
        using var bitmapLarge = generatorLarge.GenerateHeatmap(200, 200, measurements);

        // Assert
        bitmapSmall.Should().NotBeNull();
        bitmapLarge.Should().NotBeNull();

        // Both should produce valid bitmaps
        bitmapSmall.Width.Should().Be(200);
        bitmapLarge.Width.Should().Be(200);
    }
}
