using System.Text.Json;
using System.Text.Json.Serialization;

namespace WiFiSurvey.Models;

/// <summary>
/// Represents a Wi-Fi survey project containing floor plan and measurements
/// </summary>
public class SurveyProject
{
    /// <summary>
    /// Project file format version
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Project name
    /// </summary>
    public string Name { get; set; } = "New Survey";

    /// <summary>
    /// Project description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// When the project was created
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    /// <summary>
    /// When the project was last modified
    /// </summary>
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Person who created/owns the survey
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Location/site being surveyed
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// The floor plan for this survey
    /// </summary>
    public FloorPlan FloorPlan { get; set; } = new();

    /// <summary>
    /// All measurement points in this survey
    /// </summary>
    public List<MeasurementPoint> MeasurementPoints { get; set; } = new();

    /// <summary>
    /// Target SSID for the survey (filters results)
    /// </summary>
    public string TargetSSID { get; set; } = string.Empty;

    /// <summary>
    /// Project file path (not serialized)
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>
    /// Whether project has unsaved changes
    /// </summary>
    [JsonIgnore]
    public bool IsDirty { get; set; }

    /// <summary>
    /// Embedded floor plan image data (Base64 encoded)
    /// </summary>
    public string? EmbeddedFloorPlanImage { get; set; }

    /// <summary>
    /// Adds a measurement point to the project
    /// </summary>
    public void AddMeasurement(MeasurementPoint point)
    {
        MeasurementPoints.Add(point);
        ModifiedDate = DateTime.Now;
        IsDirty = true;
    }

    /// <summary>
    /// Removes a measurement point from the project
    /// </summary>
    public bool RemoveMeasurement(Guid pointId)
    {
        var point = MeasurementPoints.FirstOrDefault(p => p.Id == pointId);
        if (point != null)
        {
            MeasurementPoints.Remove(point);
            ModifiedDate = DateTime.Now;
            IsDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all measurement points
    /// </summary>
    public void ClearMeasurements()
    {
        MeasurementPoints.Clear();
        ModifiedDate = DateTime.Now;
        IsDirty = true;
    }

    /// <summary>
    /// Gets statistics about the survey
    /// </summary>
    public SurveyStatistics GetStatistics()
    {
        if (MeasurementPoints.Count == 0)
        {
            return new SurveyStatistics();
        }

        var points = MeasurementPoints;
        if (!string.IsNullOrEmpty(TargetSSID))
        {
            points = points.Where(p => p.SSID == TargetSSID).ToList();
        }

        if (points.Count == 0)
        {
            return new SurveyStatistics();
        }

        return new SurveyStatistics
        {
            TotalPoints = points.Count,
            AverageSignalStrength = points.Average(p => p.SignalStrength),
            MinSignalStrength = points.Min(p => p.SignalStrength),
            MaxSignalStrength = points.Max(p => p.SignalStrength),
            AverageLinkQuality = points.Average(p => p.LinkQuality),
            UniqueSSIDs = points.Select(p => p.SSID).Distinct().Count(),
            UniqueChannels = points.Select(p => p.Channel).Distinct().Count(),
            ExcellentCoverage = points.Count(p => p.SignalStrength >= -50),
            GoodCoverage = points.Count(p => p.SignalStrength >= -60 && p.SignalStrength < -50),
            FairCoverage = points.Count(p => p.SignalStrength >= -70 && p.SignalStrength < -60),
            WeakCoverage = points.Count(p => p.SignalStrength >= -80 && p.SignalStrength < -70),
            PoorCoverage = points.Count(p => p.SignalStrength < -80)
        };
    }

    /// <summary>
    /// Saves the project to a file
    /// </summary>
    public void Save(string filePath)
    {
        // Embed the floor plan image if available
        if (FloorPlan.Image != null)
        {
            using var stream = new MemoryStream();
            FloorPlan.Image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            EmbeddedFloorPlanImage = Convert.ToBase64String(stream.ToArray());
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(filePath, json);

        FilePath = filePath;
        IsDirty = false;
    }

    /// <summary>
    /// Loads a project from a file
    /// </summary>
    public static SurveyProject? Load(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            string json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var project = JsonSerializer.Deserialize<SurveyProject>(json, options);
            if (project != null)
            {
                project.FilePath = filePath;
                project.IsDirty = false;

                // Load embedded floor plan image
                if (!string.IsNullOrEmpty(project.EmbeddedFloorPlanImage))
                {
                    byte[] imageData = Convert.FromBase64String(project.EmbeddedFloorPlanImage);
                    project.FloorPlan.LoadFromBytes(imageData);
                }
                // Or load from file path if available
                else if (!string.IsNullOrEmpty(project.FloorPlan.ImagePath))
                {
                    project.FloorPlan.LoadImage();
                }
            }

            return project;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new project with a floor plan
    /// </summary>
    public static SurveyProject CreateNew(string name, string floorPlanPath)
    {
        var project = new SurveyProject
        {
            Name = name,
            FloorPlan = FloorPlan.FromFile(floorPlanPath) ?? new FloorPlan()
        };

        return project;
    }
}

/// <summary>
/// Statistics about a Wi-Fi survey
/// </summary>
public class SurveyStatistics
{
    public int TotalPoints { get; set; }
    public double AverageSignalStrength { get; set; }
    public int MinSignalStrength { get; set; }
    public int MaxSignalStrength { get; set; }
    public double AverageLinkQuality { get; set; }
    public int UniqueSSIDs { get; set; }
    public int UniqueChannels { get; set; }
    public int ExcellentCoverage { get; set; }
    public int GoodCoverage { get; set; }
    public int FairCoverage { get; set; }
    public int WeakCoverage { get; set; }
    public int PoorCoverage { get; set; }

    public double CoveragePercentage(string quality)
    {
        if (TotalPoints == 0) return 0;
        return quality switch
        {
            "Excellent" => ExcellentCoverage * 100.0 / TotalPoints,
            "Good" => GoodCoverage * 100.0 / TotalPoints,
            "Fair" => FairCoverage * 100.0 / TotalPoints,
            "Weak" => WeakCoverage * 100.0 / TotalPoints,
            "Poor" => PoorCoverage * 100.0 / TotalPoints,
            _ => 0
        };
    }
}
