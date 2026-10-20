using FluentAssertions;
using System.Drawing;
using WiFiSurvey.Models;
using WiFiSurvey.Services;

namespace WiFiSurvey.Tests;

/// <summary>
/// Unit tests for MeasurementPoint model
/// Tests factory methods, signal quality classification, and channel/frequency calculations
/// </summary>
public class MeasurementPointTests
{
    [Fact]
    public void Constructor_CreatesNewMeasurementPoint_WithDefaultValues()
    {
        // Arrange & Act
        var point = new MeasurementPoint();

        // Assert
        point.Should().NotBeNull();
        point.Id.Should().NotBe(Guid.Empty);
        point.X.Should().Be(0);
        point.Y.Should().Be(0);
        point.SSID.Should().Be(string.Empty);
        point.VisibleNetworks.Should().NotBeNull();
        point.VisibleNetworks.Should().BeEmpty();
    }

    [Fact]
    public void FromMeasurement_WithValidMeasurement_CreatesCorrectMeasurementPoint()
    {
        // Arrange
        var network = new WiFiNetwork
        {
            SSID = "TestNetwork",
            BSSID = "AA:BB:CC:DD:EE:FF",
            SignalStrength = -55,
            LinkQuality = 80,
            Channel = 6,
            Frequency = 2437, // Frequency will compute Band = "2.4 GHz"
            MaxRate = 866.7
        };

        var measurement = new WiFiMeasurement
        {
            Timestamp = new DateTime(2024, 1, 15, 10, 30, 0),
            ConnectedNetwork = network,
            VisibleNetworks = new List<WiFiNetwork> { network },
            SignalStrength = -55,
            LinkQuality = 80,
            Channel = 6
        };

        // Act
        var point = MeasurementPoint.FromMeasurement(0.5, 0.75, measurement);

        // Assert
        point.Should().NotBeNull();
        point.X.Should().Be(0.5);
        point.Y.Should().Be(0.75);
        point.Timestamp.Should().Be(new DateTime(2024, 1, 15, 10, 30, 0));
        point.SignalStrength.Should().Be(-55);
        point.LinkQuality.Should().Be(80);
        point.Channel.Should().Be(6);
        point.SSID.Should().Be("TestNetwork");
        point.BSSID.Should().Be("AA:BB:CC:DD:EE:FF");
        point.Band.Should().Be("2.4 GHz");
        point.Frequency.Should().Be(2437);
        point.MaxRate.Should().Be(866.7);
        point.VisibleNetworks.Should().HaveCount(1);
    }

    [Fact]
    public void FromMeasurement_WithNullConnectedNetwork_UsesDefaultValues()
    {
        // Arrange
        var measurement = new WiFiMeasurement
        {
            Timestamp = DateTime.Now,
            ConnectedNetwork = null,
            VisibleNetworks = new List<WiFiNetwork>(),
            SignalStrength = -100,
            LinkQuality = 0,
            Channel = 0
        };

        // Act
        var point = MeasurementPoint.FromMeasurement(0.3, 0.4, measurement);

        // Assert
        point.Should().NotBeNull();
        point.SSID.Should().Be(string.Empty);
        point.BSSID.Should().Be(string.Empty);
        point.Band.Should().Be(string.Empty);
        point.Frequency.Should().Be(0);
        point.MaxRate.Should().Be(0);
    }

    [Theory]
    [InlineData(-40, 0, 200, 0)] // Excellent - Green
    [InlineData(-55, 150, 200, 0)] // Good - Yellow-Green
    [InlineData(-65, 255, 200, 0)] // Fair - Yellow-Orange
    [InlineData(-80, 255, 50, 0)] // Weak/Poor - Red
    public void GetSignalColor_WithVariousSignalStrengths_ReturnsCorrectColor(
        int signalStrength, int expectedR, int expectedG, int expectedB)
    {
        // Arrange
        var point = new MeasurementPoint
        {
            SignalStrength = signalStrength
        };

        // Act
        var color = point.GetSignalColor();

        // Assert
        color.R.Should().Be((byte)expectedR);
        color.G.Should().Be((byte)expectedG);
        color.B.Should().Be((byte)expectedB);
    }

    [Fact]
    public void GetSignalColor_WithExcellentSignal_ReturnsGreen()
    {
        // Arrange
        var point = new MeasurementPoint { SignalStrength = -35 };

        // Act
        var color = point.GetSignalColor();

        // Assert
        color.G.Should().BeGreaterThan(color.R);
        color.G.Should().BeGreaterThan(color.B);
    }

    [Fact]
    public void GetSignalColor_WithPoorSignal_ReturnsRed()
    {
        // Arrange
        var point = new MeasurementPoint { SignalStrength = -85 };

        // Act
        var color = point.GetSignalColor();

        // Assert
        color.R.Should().BeGreaterThan(color.G);
        color.R.Should().BeGreaterThan(color.B);
    }

    [Theory]
    [InlineData(-45, "Excellent")]
    [InlineData(-50, "Excellent")]
    [InlineData(-55, "Good")]
    [InlineData(-60, "Good")]
    [InlineData(-65, "Fair")]
    [InlineData(-70, "Fair")]
    [InlineData(-75, "Weak")]
    [InlineData(-80, "Weak")]
    [InlineData(-85, "Poor")]
    [InlineData(-95, "Poor")]
    public void QualityDescription_WithVariousSignalStrengths_ReturnsCorrectDescription(
        int signalStrength, string expectedDescription)
    {
        // Arrange
        var point = new MeasurementPoint { SignalStrength = signalStrength };

        // Act
        var description = point.QualityDescription;

        // Assert
        description.Should().Be(expectedDescription);
    }

    [Fact]
    public void QualityDescription_Excellent_ThresholdAt50()
    {
        // Arrange
        var pointBoundary = new MeasurementPoint { SignalStrength = -50 };
        var pointAbove = new MeasurementPoint { SignalStrength = -49 };
        var pointBelow = new MeasurementPoint { SignalStrength = -51 };

        // Act & Assert
        pointBoundary.QualityDescription.Should().Be("Excellent");
        pointAbove.QualityDescription.Should().Be("Excellent");
        pointBelow.QualityDescription.Should().Be("Good");
    }

    [Fact]
    public void QualityDescription_Good_ThresholdAt60()
    {
        // Arrange
        var pointBoundary = new MeasurementPoint { SignalStrength = -60 };
        var pointAbove = new MeasurementPoint { SignalStrength = -59 };
        var pointBelow = new MeasurementPoint { SignalStrength = -61 };

        // Act & Assert
        pointBoundary.QualityDescription.Should().Be("Good");
        pointAbove.QualityDescription.Should().Be("Good");
        pointBelow.QualityDescription.Should().Be("Fair");
    }

    [Fact]
    public void QualityDescription_Fair_ThresholdAt70()
    {
        // Arrange
        var pointBoundary = new MeasurementPoint { SignalStrength = -70 };
        var pointAbove = new MeasurementPoint { SignalStrength = -69 };
        var pointBelow = new MeasurementPoint { SignalStrength = -71 };

        // Act & Assert
        pointBoundary.QualityDescription.Should().Be("Fair");
        pointAbove.QualityDescription.Should().Be("Fair");
        pointBelow.QualityDescription.Should().Be("Weak");
    }

    [Fact]
    public void QualityDescription_Weak_ThresholdAt80()
    {
        // Arrange
        var pointBoundary = new MeasurementPoint { SignalStrength = -80 };
        var pointAbove = new MeasurementPoint { SignalStrength = -79 };
        var pointBelow = new MeasurementPoint { SignalStrength = -81 };

        // Act & Assert
        pointBoundary.QualityDescription.Should().Be("Weak");
        pointAbove.QualityDescription.Should().Be("Weak");
        pointBelow.QualityDescription.Should().Be("Poor");
    }

    [Fact]
    public void ToString_WithCompleteData_ReturnsFormattedString()
    {
        // Arrange
        var point = new MeasurementPoint
        {
            X = 0.5,
            Y = 0.75,
            SSID = "TestNetwork",
            SignalStrength = -55
        };

        // Act
        var result = point.ToString();

        // Assert
        result.Should().Contain("TestNetwork");
        result.Should().Contain("0.50");
        result.Should().Contain("0.75");
        result.Should().Contain("-55 dBm");
        result.Should().Contain("Good");
    }

    [Fact]
    public void Id_IsUniqueForEachInstance()
    {
        // Arrange & Act
        var point1 = new MeasurementPoint();
        var point2 = new MeasurementPoint();

        // Assert
        point1.Id.Should().NotBe(point2.Id);
        point1.Id.Should().NotBe(Guid.Empty);
        point2.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Timestamp_DefaultsToCurrentTime()
    {
        // Arrange
        var beforeCreation = DateTime.Now.AddSeconds(-1);

        // Act
        var point = new MeasurementPoint();
        var afterCreation = DateTime.Now.AddSeconds(1);

        // Assert
        point.Timestamp.Should().BeAfter(beforeCreation);
        point.Timestamp.Should().BeBefore(afterCreation);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var point = new MeasurementPoint();
        var testTime = new DateTime(2024, 6, 15, 14, 30, 0);

        // Act
        point.X = 0.25;
        point.Y = 0.75;
        point.Timestamp = testTime;
        point.SignalStrength = -60;
        point.LinkQuality = 75;
        point.Channel = 11;
        point.SSID = "MyNetwork";
        point.BSSID = "11:22:33:44:55:66";
        point.Band = "5 GHz";
        point.Frequency = 5180;
        point.MaxRate = 1200;
        point.Note = "Conference Room";

        // Assert
        point.X.Should().Be(0.25);
        point.Y.Should().Be(0.75);
        point.Timestamp.Should().Be(testTime);
        point.SignalStrength.Should().Be(-60);
        point.LinkQuality.Should().Be(75);
        point.Channel.Should().Be(11);
        point.SSID.Should().Be("MyNetwork");
        point.BSSID.Should().Be("11:22:33:44:55:66");
        point.Band.Should().Be("5 GHz");
        point.Frequency.Should().Be(5180);
        point.MaxRate.Should().Be(1200);
        point.Note.Should().Be("Conference Room");
    }

    [Fact]
    public void VisibleNetworks_CanAddNetworks()
    {
        // Arrange
        var point = new MeasurementPoint();
        var network1 = new WiFiNetwork { SSID = "Network1" };
        var network2 = new WiFiNetwork { SSID = "Network2" };

        // Act
        point.VisibleNetworks.Add(network1);
        point.VisibleNetworks.Add(network2);

        // Assert
        point.VisibleNetworks.Should().HaveCount(2);
        point.VisibleNetworks[0].SSID.Should().Be("Network1");
        point.VisibleNetworks[1].SSID.Should().Be("Network2");
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.123, 0.456)]
    public void NormalizedCoordinates_AcceptsValidRange(double x, double y)
    {
        // Arrange & Act
        var point = new MeasurementPoint { X = x, Y = y };

        // Assert
        point.X.Should().Be(x);
        point.Y.Should().Be(y);
    }

    [Fact]
    public void SignalStrength_AcceptsTypicalWiFiRange()
    {
        // Arrange
        var point = new MeasurementPoint();

        // Act & Assert - typical Wi-Fi RSSI range is -30 to -90 dBm
        point.SignalStrength = -30;
        point.SignalStrength.Should().Be(-30);

        point.SignalStrength = -90;
        point.SignalStrength.Should().Be(-90);

        point.SignalStrength = -60;
        point.SignalStrength.Should().Be(-60);
    }

    [Fact]
    public void LinkQuality_AcceptsPercentageRange()
    {
        // Arrange
        var point = new MeasurementPoint();

        // Act & Assert
        point.LinkQuality = 0;
        point.LinkQuality.Should().Be(0);

        point.LinkQuality = 100;
        point.LinkQuality.Should().Be(100);

        point.LinkQuality = 50;
        point.LinkQuality.Should().Be(50);
    }
}
