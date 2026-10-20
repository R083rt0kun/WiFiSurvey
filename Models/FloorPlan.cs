using System.Text.Json.Serialization;

namespace WiFiSurvey.Models;

/// <summary>
/// Represents a floor plan image with scale information
/// </summary>
public class FloorPlan
{
    /// <summary>
    /// Path to the floor plan image file
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Original image width in pixels
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// Original image height in pixels
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// Scale: real-world distance per pixel (in meters)
    /// </summary>
    public double MetersPerPixel { get; set; } = 0.05; // Default: 5cm per pixel

    /// <summary>
    /// Name/label for this floor plan
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional notes about the floor plan
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// The floor plan image (not serialized)
    /// </summary>
    [JsonIgnore]
    public Image? Image { get; set; }

    /// <summary>
    /// Loads the floor plan image from the specified path
    /// </summary>
    public bool LoadImage()
    {
        if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
            return false;

        try
        {
            // Dispose existing image if any
            Image?.Dispose();

            // Load new image
            Image = System.Drawing.Image.FromFile(ImagePath);
            ImageWidth = Image.Width;
            ImageHeight = Image.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Loads floor plan from image bytes (for embedded images)
    /// </summary>
    public bool LoadFromBytes(byte[] imageData)
    {
        try
        {
            Image?.Dispose();
            using var stream = new MemoryStream(imageData);
            Image = System.Drawing.Image.FromStream(stream);
            ImageWidth = Image.Width;
            ImageHeight = Image.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a floor plan from an image file
    /// </summary>
    public static FloorPlan? FromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var floorPlan = new FloorPlan
        {
            ImagePath = filePath,
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

        if (floorPlan.LoadImage())
            return floorPlan;

        return null;
    }

    /// <summary>
    /// Converts pixel coordinates to real-world meters
    /// </summary>
    public (double x, double y) PixelsToMeters(int pixelX, int pixelY)
    {
        return (pixelX * MetersPerPixel, pixelY * MetersPerPixel);
    }

    /// <summary>
    /// Converts real-world meters to pixel coordinates
    /// </summary>
    public (int x, int y) MetersToPixels(double meterX, double meterY)
    {
        return ((int)(meterX / MetersPerPixel), (int)(meterY / MetersPerPixel));
    }

    /// <summary>
    /// Gets the total area in square meters
    /// </summary>
    public double TotalAreaSquareMeters => (ImageWidth * MetersPerPixel) * (ImageHeight * MetersPerPixel);

    /// <summary>
    /// Gets the dimensions in meters
    /// </summary>
    public (double width, double height) DimensionsMeters =>
        (ImageWidth * MetersPerPixel, ImageHeight * MetersPerPixel);

    /// <summary>
    /// Disposes of the floor plan image
    /// </summary>
    public void Dispose()
    {
        Image?.Dispose();
        Image = null;
    }
}
