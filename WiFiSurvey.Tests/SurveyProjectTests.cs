using FluentAssertions;
using System.Text.Json;
using WiFiSurvey.Models;

namespace WiFiSurvey.Tests;

/// <summary>
/// Unit tests for SurveyProject
/// Tests JSON serialization, statistics calculation, and measurement management
/// </summary>
public class SurveyProjectTests
{
    private readonly string _tempDirectory;

    public SurveyProjectTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WiFiSurveyTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Constructor_CreatesNewProject_WithDefaultValues()
    {
        // Arrange & Act
        var project = new SurveyProject();

        // Assert
        project.Should().NotBeNull();
        project.Version.Should().Be("1.0");
        project.Name.Should().Be("New Survey");
        project.Description.Should().Be(string.Empty);
        project.Author.Should().Be(string.Empty);
        project.MeasurementPoints.Should().NotBeNull();
        project.MeasurementPoints.Should().BeEmpty();
        project.FloorPlan.Should().NotBeNull();
        project.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void AddMeasurement_AddsPointToCollection_AndMarksDirty()
    {
        // Arrange
        var project = new SurveyProject();
        var point = new MeasurementPoint
        {
            X = 0.5,
            Y = 0.5,
            SignalStrength = -50,
            SSID = "TestNetwork"
        };

        // Act
        var beforeAdd = project.ModifiedDate;
        Thread.Sleep(10); // Ensure time difference
        project.AddMeasurement(point);

        // Assert
        project.MeasurementPoints.Should().HaveCount(1);
        project.MeasurementPoints[0].Should().Be(point);
        project.IsDirty.Should().BeTrue();
        project.ModifiedDate.Should().BeAfter(beforeAdd);
    }

    [Fact]
    public void RemoveMeasurement_RemovesExistingPoint_AndMarksDirty()
    {
        // Arrange
        var project = new SurveyProject();
        var point = new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 };
        project.AddMeasurement(point);
        project.IsDirty = false;

        // Act
        var result = project.RemoveMeasurement(point.Id);

        // Assert
        result.Should().BeTrue();
        project.MeasurementPoints.Should().BeEmpty();
        project.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void RemoveMeasurement_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var project = new SurveyProject();
        var point = new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 };
        project.AddMeasurement(point);
        project.IsDirty = false;

        // Act
        var result = project.RemoveMeasurement(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
        project.MeasurementPoints.Should().HaveCount(1);
        project.IsDirty.Should().BeFalse(); // Should not mark dirty if nothing removed
    }

    [Fact]
    public void ClearMeasurements_RemovesAllPoints_AndMarksDirty()
    {
        // Arrange
        var project = new SurveyProject();
        project.AddMeasurement(new MeasurementPoint { X = 0.1, Y = 0.1, SignalStrength = -40 });
        project.AddMeasurement(new MeasurementPoint { X = 0.5, Y = 0.5, SignalStrength = -50 });
        project.AddMeasurement(new MeasurementPoint { X = 0.9, Y = 0.9, SignalStrength = -60 });
        project.IsDirty = false;

        // Act
        project.ClearMeasurements();

        // Assert
        project.MeasurementPoints.Should().BeEmpty();
        project.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void GetStatistics_WithNoMeasurements_ReturnsEmptyStatistics()
    {
        // Arrange
        var project = new SurveyProject();

        // Act
        var stats = project.GetStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalPoints.Should().Be(0);
        stats.AverageSignalStrength.Should().Be(0);
    }

    [Fact]
    public void GetStatistics_WithMeasurements_CalculatesCorrectStatistics()
    {
        // Arrange
        var project = new SurveyProject();
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.1, Y = 0.1, SignalStrength = -40, LinkQuality = 90,
            SSID = "Network1", Channel = 6
        });
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.5, Y = 0.5, SignalStrength = -60, LinkQuality = 70,
            SSID = "Network2", Channel = 11
        });
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.9, Y = 0.9, SignalStrength = -80, LinkQuality = 50,
            SSID = "Network1", Channel = 6
        });

        // Act
        var stats = project.GetStatistics();

        // Assert
        stats.TotalPoints.Should().Be(3);
        stats.AverageSignalStrength.Should().Be(-60); // (-40 + -60 + -80) / 3
        stats.MinSignalStrength.Should().Be(-80);
        stats.MaxSignalStrength.Should().Be(-40);
        stats.AverageLinkQuality.Should().Be(70); // (90 + 70 + 50) / 3
        stats.UniqueSSIDs.Should().Be(2);
        stats.UniqueChannels.Should().Be(2);
        stats.ExcellentCoverage.Should().Be(1); // -40
        stats.GoodCoverage.Should().Be(1); // -60
        stats.WeakCoverage.Should().Be(1); // -80
    }

    [Fact]
    public void GetStatistics_WithTargetSSID_FiltersCorrectly()
    {
        // Arrange
        var project = new SurveyProject { TargetSSID = "Network1" };
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.1, Y = 0.1, SignalStrength = -40, SSID = "Network1"
        });
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.5, Y = 0.5, SignalStrength = -60, SSID = "Network2"
        });
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.9, Y = 0.9, SignalStrength = -50, SSID = "Network1"
        });

        // Act
        var stats = project.GetStatistics();

        // Assert
        stats.TotalPoints.Should().Be(2); // Only Network1 points
        stats.AverageSignalStrength.Should().Be(-45); // (-40 + -50) / 2
    }

    [Fact]
    public void Save_CreatesJsonFile_WithCorrectFormat()
    {
        // Arrange
        var project = new SurveyProject
        {
            Name = "Test Survey",
            Description = "Test Description",
            Location = "Test Location",
            TargetSSID = "TestSSID"
        };
        project.AddMeasurement(new MeasurementPoint
        {
            X = 0.5, Y = 0.5, SignalStrength = -50, SSID = "TestSSID"
        });

        var filePath = Path.Combine(_tempDirectory, "test_project.json");

        // Act
        project.Save(filePath);

        // Assert
        File.Exists(filePath).Should().BeTrue();
        project.FilePath.Should().Be(filePath);
        project.IsDirty.Should().BeFalse();

        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"name\": \"Test Survey\"");
        json.Should().Contain("\"description\": \"Test Description\"");
        json.Should().Contain("\"targetSSID\": \"TestSSID\"");
    }

    [Fact]
    public void Load_WithValidFile_ReturnsProject()
    {
        // Arrange
        var originalProject = new SurveyProject
        {
            Name = "Original Survey",
            Description = "Original Description",
            Location = "Original Location",
            TargetSSID = "OriginalSSID"
        };
        originalProject.AddMeasurement(new MeasurementPoint
        {
            X = 0.25, Y = 0.75, SignalStrength = -55, SSID = "OriginalSSID",
            LinkQuality = 80, Channel = 6
        });

        var filePath = Path.Combine(_tempDirectory, "original_project.json");
        originalProject.Save(filePath);

        // Act
        var loadedProject = SurveyProject.Load(filePath);

        // Assert
        loadedProject.Should().NotBeNull();
        loadedProject!.Name.Should().Be("Original Survey");
        loadedProject.Description.Should().Be("Original Description");
        loadedProject.Location.Should().Be("Original Location");
        loadedProject.TargetSSID.Should().Be("OriginalSSID");
        loadedProject.MeasurementPoints.Should().HaveCount(1);
        loadedProject.MeasurementPoints[0].X.Should().Be(0.25);
        loadedProject.MeasurementPoints[0].Y.Should().Be(0.75);
        loadedProject.MeasurementPoints[0].SignalStrength.Should().Be(-55);
        loadedProject.FilePath.Should().Be(filePath);
        loadedProject.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Load_WithNonExistentFile_ReturnsNull()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "nonexistent.json");

        // Act
        var loadedProject = SurveyProject.Load(filePath);

        // Assert
        loadedProject.Should().BeNull();
    }

    [Fact]
    public void Load_WithInvalidJson_ReturnsNull()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "invalid.json");
        File.WriteAllText(filePath, "{ invalid json content }");

        // Act
        var loadedProject = SurveyProject.Load(filePath);

        // Assert
        loadedProject.Should().BeNull();
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesData()
    {
        // Arrange
        var originalProject = new SurveyProject
        {
            Name = "RoundTrip Survey",
            Description = "Testing serialization",
            Location = "Test Lab",
            TargetSSID = "TestNetwork",
            Author = "Test Author"
        };

        // Add multiple measurement points
        originalProject.AddMeasurement(new MeasurementPoint
        {
            X = 0.1, Y = 0.1, SignalStrength = -40, LinkQuality = 90,
            SSID = "TestNetwork", BSSID = "AA:BB:CC:DD:EE:FF",
            Channel = 6, Frequency = 2437, MaxRate = 866.7,
            Band = "2.4 GHz", Note = "Corner office"
        });
        originalProject.AddMeasurement(new MeasurementPoint
        {
            X = 0.9, Y = 0.9, SignalStrength = -80, LinkQuality = 40,
            SSID = "TestNetwork", BSSID = "AA:BB:CC:DD:EE:FF",
            Channel = 6, Frequency = 2437, MaxRate = 866.7,
            Band = "2.4 GHz", Note = "Far corner"
        });

        var filePath = Path.Combine(_tempDirectory, "roundtrip.json");

        // Act
        originalProject.Save(filePath);
        var loadedProject = SurveyProject.Load(filePath);

        // Assert
        loadedProject.Should().NotBeNull();
        loadedProject!.Name.Should().Be(originalProject.Name);
        loadedProject.Description.Should().Be(originalProject.Description);
        loadedProject.Location.Should().Be(originalProject.Location);
        loadedProject.TargetSSID.Should().Be(originalProject.TargetSSID);
        loadedProject.Author.Should().Be(originalProject.Author);
        loadedProject.MeasurementPoints.Should().HaveCount(2);

        // Check first measurement point
        var point1 = loadedProject.MeasurementPoints[0];
        point1.X.Should().Be(0.1);
        point1.Y.Should().Be(0.1);
        point1.SignalStrength.Should().Be(-40);
        point1.SSID.Should().Be("TestNetwork");
        point1.Note.Should().Be("Corner office");

        // Check second measurement point
        var point2 = loadedProject.MeasurementPoints[1];
        point2.X.Should().Be(0.9);
        point2.Y.Should().Be(0.9);
        point2.SignalStrength.Should().Be(-80);
        point2.Note.Should().Be("Far corner");
    }

    [Fact]
    public void SurveyStatistics_CoveragePercentage_CalculatesCorrectly()
    {
        // Arrange
        var stats = new SurveyStatistics
        {
            TotalPoints = 100,
            ExcellentCoverage = 20,
            GoodCoverage = 30,
            FairCoverage = 25,
            WeakCoverage = 15,
            PoorCoverage = 10
        };

        // Act & Assert
        stats.CoveragePercentage("Excellent").Should().Be(20.0);
        stats.CoveragePercentage("Good").Should().Be(30.0);
        stats.CoveragePercentage("Fair").Should().Be(25.0);
        stats.CoveragePercentage("Weak").Should().Be(15.0);
        stats.CoveragePercentage("Poor").Should().Be(10.0);
        stats.CoveragePercentage("Unknown").Should().Be(0);
    }

    [Fact]
    public void SurveyStatistics_CoveragePercentage_WithZeroPoints_ReturnsZero()
    {
        // Arrange
        var stats = new SurveyStatistics { TotalPoints = 0 };

        // Act & Assert
        stats.CoveragePercentage("Excellent").Should().Be(0);
        stats.CoveragePercentage("Good").Should().Be(0);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var project = new SurveyProject();
        var createdDate = new DateTime(2024, 1, 1);

        // Act
        project.Version = "2.0";
        project.Name = "Custom Survey";
        project.Description = "Custom Description";
        project.CreatedDate = createdDate;
        project.Author = "Custom Author";
        project.Location = "Custom Location";
        project.TargetSSID = "CustomSSID";

        // Assert
        project.Version.Should().Be("2.0");
        project.Name.Should().Be("Custom Survey");
        project.Description.Should().Be("Custom Description");
        project.CreatedDate.Should().Be(createdDate);
        project.Author.Should().Be("Custom Author");
        project.Location.Should().Be("Custom Location");
        project.TargetSSID.Should().Be("CustomSSID");
    }

    [Fact]
    public void GetStatistics_CoverageClassification_IsCorrect()
    {
        // Arrange
        var project = new SurveyProject();
        project.AddMeasurement(new MeasurementPoint { SignalStrength = -45, SSID = "Test" }); // Excellent
        project.AddMeasurement(new MeasurementPoint { SignalStrength = -55, SSID = "Test" }); // Good
        project.AddMeasurement(new MeasurementPoint { SignalStrength = -65, SSID = "Test" }); // Fair
        project.AddMeasurement(new MeasurementPoint { SignalStrength = -75, SSID = "Test" }); // Weak
        project.AddMeasurement(new MeasurementPoint { SignalStrength = -85, SSID = "Test" }); // Poor

        // Act
        var stats = project.GetStatistics();

        // Assert
        stats.ExcellentCoverage.Should().Be(1);
        stats.GoodCoverage.Should().Be(1);
        stats.FairCoverage.Should().Be(1);
        stats.WeakCoverage.Should().Be(1);
        stats.PoorCoverage.Should().Be(1);
    }

    // Cleanup after tests
    ~SurveyProjectTests()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
