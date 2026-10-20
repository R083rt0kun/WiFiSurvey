using System.Text.Json.Serialization;
using WiFiSurvey.Services;

namespace WiFiSurvey.Models;

/// <summary>
/// Represents a Wi-Fi measurement point on the floor plan
/// </summary>
public class MeasurementPoint
{
    /// <summary>
    /// Unique identifier for this measurement point
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// X coordinate on the floor plan (0-1 normalized)
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y coordinate on the floor plan (0-1 normalized)
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// When this measurement was taken
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Signal strength in dBm
    /// </summary>
    public int SignalStrength { get; set; }

    /// <summary>
    /// Link quality percentage (0-100)
    /// </summary>
    public int LinkQuality { get; set; }

    /// <summary>
    /// Wi-Fi channel
    /// </summary>
    public int Channel { get; set; }

    /// <summary>
    /// SSID of the network measured
    /// </summary>
    public string SSID { get; set; } = string.Empty;

    /// <summary>
    /// BSSID (MAC address) of the access point
    /// </summary>
    public string BSSID { get; set; } = string.Empty;

    /// <summary>
    /// Frequency band (2.4 GHz, 5 GHz, 6 GHz)
    /// </summary>
    public string Band { get; set; } = string.Empty;

    /// <summary>
    /// Frequency in MHz
    /// </summary>
    public double Frequency { get; set; }

    /// <summary>
    /// Maximum data rate in Mbps
    /// </summary>
    public double MaxRate { get; set; }

    /// <summary>
    /// Optional note about this measurement location
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// All networks visible at this point
    /// </summary>
    [JsonIgnore]
    public List<WiFiNetwork> VisibleNetworks { get; set; } = new();

    /// <summary>
    /// Creates a measurement point from a Wi-Fi measurement
    /// </summary>
    public static MeasurementPoint FromMeasurement(double x, double y, WiFiMeasurement measurement)
    {
        return new MeasurementPoint
        {
            X = x,
            Y = y,
            Timestamp = measurement.Timestamp,
            SignalStrength = measurement.SignalStrength,
            LinkQuality = measurement.LinkQuality,
            Channel = measurement.Channel,
            SSID = measurement.ConnectedNetwork?.SSID ?? string.Empty,
            BSSID = measurement.ConnectedNetwork?.BSSID ?? string.Empty,
            Band = measurement.ConnectedNetwork?.Band ?? string.Empty,
            Frequency = measurement.ConnectedNetwork?.Frequency ?? 0,
            MaxRate = measurement.ConnectedNetwork?.MaxRate ?? 0,
            VisibleNetworks = measurement.VisibleNetworks
        };
    }

    /// <summary>
    /// Gets the color for this measurement based on signal strength
    /// </summary>
    public Color GetSignalColor()
    {
        // Color gradient from red (weak) to green (strong)
        // RSSI typically ranges from -30 (excellent) to -90 (very weak)
        double normalized = Math.Clamp((SignalStrength + 90) / 60.0, 0, 1);

        if (normalized >= 0.75) // Excellent (-30 to -45)
            return Color.FromArgb(0, 200, 0); // Green
        else if (normalized >= 0.5) // Good (-45 to -60)
            return Color.FromArgb(150, 200, 0); // Yellow-Green
        else if (normalized >= 0.25) // Fair (-60 to -75)
            return Color.FromArgb(255, 200, 0); // Yellow-Orange
        else // Weak/Poor (-75 to -90)
            return Color.FromArgb(255, 50, 0); // Red
    }

    /// <summary>
    /// Gets a description of the signal quality
    /// </summary>
    public string QualityDescription => SignalStrength switch
    {
        >= -50 => "Excellent",
        >= -60 => "Good",
        >= -70 => "Fair",
        >= -80 => "Weak",
        _ => "Poor"
    };

    public override string ToString()
    {
        return $"{SSID} @ ({X:F2}, {Y:F2}): {SignalStrength} dBm ({QualityDescription})";
    }
}
