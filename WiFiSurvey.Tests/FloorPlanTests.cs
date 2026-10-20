using FluentAssertions;
using System.Drawing;
using System.Drawing.Imaging;
using WiFiSurvey.Models;

namespace WiFiSurvey.Tests;

/// <summary>
/// Unit tests for FloorPlan model
/// Tests image loading, coordinate conversion, and disposal
/// </summary>
public class FloorPlanTests
{
    private readonly string _tempDirectory;
    private readonly string _testImagePath;

    public FloorPlanTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WiFiSurveyTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        // Create a test image
        _testImagePath = Path.Combine(_tempDirectory, "test_floorplan.png");
        CreateTestImage(_testImagePath, 800, 600);
    }

    private void CreateTestImage(string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);

        // Draw a simple test pattern
        g.Clear(Color.White);
        g.DrawRectangle(Pens.Black, 0, 0, width - 1, height - 1);
        g.DrawLine(Pens.Blue, 0, 0, width, height);

        bitmap.Save(path, ImageFormat.Png);
    }

    [Fact]
    public void Constructor_CreatesNewFloorPlan_WithDefaultValues()
    {
        // Arrange & Act
        var floorPlan = new FloorPlan();

        // Assert
        floorPlan.Should().NotBeNull();
        floorPlan.ImagePath.Should().Be(string.Empty);
        floorPlan.ImageWidth.Should().Be(0);
        floorPlan.ImageHeight.Should().Be(0);
        floorPlan.MetersPerPixel.Should().Be(0.05); // Default 5cm per pixel
        floorPlan.Name.Should().Be(string.Empty);
        floorPlan.Notes.Should().Be(string.Empty);
        floorPlan.Image.Should().BeNull();
    }

    [Fact]
    public void LoadImage_WithValidPath_LoadsImageSuccessfully()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = _testImagePath };

        // Act
        var result = floorPlan.LoadImage();

        // Assert
        result.Should().BeTrue();
        floorPlan.Image.Should().NotBeNull();
        floorPlan.ImageWidth.Should().Be(800);
        floorPlan.ImageHeight.Should().Be(600);
    }

    [Fact]
    public void LoadImage_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = "nonexistent_file.png" };

        // Act
        var result = floorPlan.LoadImage();

        // Assert
        result.Should().BeFalse();
        floorPlan.Image.Should().BeNull();
    }

    [Fact]
    public void LoadImage_WithEmptyPath_ReturnsFalse()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = string.Empty };

        // Act
        var result = floorPlan.LoadImage();

        // Assert
        result.Should().BeFalse();
        floorPlan.Image.Should().BeNull();
    }

    [Fact]
    public void LoadImage_DisposesExistingImage_BeforeLoadingNew()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = _testImagePath };
        floorPlan.LoadImage();
        var firstImage = floorPlan.Image;

        // Create a second test image
        var secondImagePath = Path.Combine(_tempDirectory, "test_floorplan2.png");
        CreateTestImage(secondImagePath, 400, 300);
        floorPlan.ImagePath = secondImagePath;

        // Act
        var result = floorPlan.LoadImage();

        // Assert
        result.Should().BeTrue();
        floorPlan.Image.Should().NotBeSameAs(firstImage);
        floorPlan.ImageWidth.Should().Be(400);
        floorPlan.ImageHeight.Should().Be(300);
    }

    [Fact]
    public void LoadFromBytes_WithValidImageData_LoadsSuccessfully()
    {
        // Arrange
        var floorPlan = new FloorPlan();
        byte[] imageData = File.ReadAllBytes(_testImagePath);

        // Act
        var result = floorPlan.LoadFromBytes(imageData);

        // Assert
        result.Should().BeTrue();
        floorPlan.Image.Should().NotBeNull();
        floorPlan.ImageWidth.Should().Be(800);
        floorPlan.ImageHeight.Should().Be(600);
    }

    [Fact]
    public void LoadFromBytes_WithInvalidData_ReturnsFalse()
    {
        // Arrange
        var floorPlan = new FloorPlan();
        byte[] invalidData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = floorPlan.LoadFromBytes(invalidData);

        // Assert
        result.Should().BeFalse();
        floorPlan.Image.Should().BeNull();
    }

    [Fact]
    public void LoadFromBytes_DisposesExistingImage_BeforeLoadingNew()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = _testImagePath };
        floorPlan.LoadImage();
        var firstImage = floorPlan.Image;

        byte[] imageData = File.ReadAllBytes(_testImagePath);

        // Act
        var result = floorPlan.LoadFromBytes(imageData);

        // Assert
        result.Should().BeTrue();
        floorPlan.Image.Should().NotBeSameAs(firstImage);
    }

    [Fact]
    public void FromFile_WithValidFile_CreatesFloorPlan()
    {
        // Act
        var floorPlan = FloorPlan.FromFile(_testImagePath);

        // Assert
        floorPlan.Should().NotBeNull();
        floorPlan!.ImagePath.Should().Be(_testImagePath);
        floorPlan.Name.Should().Be("test_floorplan");
        floorPlan.Image.Should().NotBeNull();
        floorPlan.ImageWidth.Should().Be(800);
        floorPlan.ImageHeight.Should().Be(600);
    }

    [Fact]
    public void FromFile_WithNonExistentFile_ReturnsNull()
    {
        // Act
        var floorPlan = FloorPlan.FromFile("nonexistent.png");

        // Assert
        floorPlan.Should().BeNull();
    }

    [Fact]
    public void FromFile_WithInvalidImageFile_ReturnsNull()
    {
        // Arrange
        var invalidFile = Path.Combine(_tempDirectory, "invalid.png");
        File.WriteAllText(invalidFile, "Not an image file");

        // Act
        var floorPlan = FloorPlan.FromFile(invalidFile);

        // Assert
        floorPlan.Should().BeNull();
    }

    [Fact]
    public void PixelsToMeters_ConvertsCorrectly()
    {
        // Arrange
        var floorPlan = new FloorPlan { MetersPerPixel = 0.1 }; // 10cm per pixel

        // Act
        var (x, y) = floorPlan.PixelsToMeters(100, 200);

        // Assert
        x.Should().Be(10.0); // 100 pixels * 0.1 = 10 meters
        y.Should().Be(20.0); // 200 pixels * 0.1 = 20 meters
    }

    [Fact]
    public void PixelsToMeters_WithDefaultScale_UsesDefaultMetersPerPixel()
    {
        // Arrange
        var floorPlan = new FloorPlan(); // Default 0.05 m/pixel (5cm)

        // Act
        var (x, y) = floorPlan.PixelsToMeters(100, 200);

        // Assert
        x.Should().Be(5.0); // 100 pixels * 0.05 = 5 meters
        y.Should().Be(10.0); // 200 pixels * 0.05 = 10 meters
    }

    [Fact]
    public void MetersToPixels_ConvertsCorrectly()
    {
        // Arrange
        var floorPlan = new FloorPlan { MetersPerPixel = 0.1 }; // 10cm per pixel

        // Act
        var (x, y) = floorPlan.MetersToPixels(10.0, 20.0);

        // Assert
        x.Should().Be(100); // 10 meters / 0.1 = 100 pixels
        y.Should().Be(200); // 20 meters / 0.1 = 200 pixels
    }

    [Fact]
    public void MetersToPixels_WithDefaultScale_UsesDefaultMetersPerPixel()
    {
        // Arrange
        var floorPlan = new FloorPlan(); // Default 0.05 m/pixel (5cm)

        // Act
        var (x, y) = floorPlan.MetersToPixels(5.0, 10.0);

        // Assert
        x.Should().Be(100); // 5 meters / 0.05 = 100 pixels
        y.Should().Be(200); // 10 meters / 0.05 = 200 pixels
    }

    [Fact]
    public void PixelsToMeters_AndMetersToPixels_AreInverse()
    {
        // Arrange
        var floorPlan = new FloorPlan { MetersPerPixel = 0.075 };
        int originalX = 150;
        int originalY = 250;

        // Act
        var (metersX, metersY) = floorPlan.PixelsToMeters(originalX, originalY);
        var (pixelsX, pixelsY) = floorPlan.MetersToPixels(metersX, metersY);

        // Assert
        pixelsX.Should().Be(originalX);
        pixelsY.Should().Be(originalY);
    }

    [Fact]
    public void TotalAreaSquareMeters_CalculatesCorrectly()
    {
        // Arrange
        var floorPlan = new FloorPlan
        {
            ImageWidth = 800,
            ImageHeight = 600,
            MetersPerPixel = 0.1 // 10cm per pixel
        };

        // Act
        var area = floorPlan.TotalAreaSquareMeters;

        // Assert
        // (800 * 0.1) * (600 * 0.1) = 80 * 60 = 4800 square meters
        area.Should().Be(4800.0);
    }

    [Fact]
    public void DimensionsMeters_CalculatesCorrectly()
    {
        // Arrange
        var floorPlan = new FloorPlan
        {
            ImageWidth = 800,
            ImageHeight = 600,
            MetersPerPixel = 0.05 // 5cm per pixel
        };

        // Act
        var (width, height) = floorPlan.DimensionsMeters;

        // Assert
        width.Should().Be(40.0); // 800 * 0.05 = 40 meters
        height.Should().Be(30.0); // 600 * 0.05 = 30 meters
    }

    [Fact]
    public void Dispose_DisposesImage_AndSetsToNull()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = _testImagePath };
        floorPlan.LoadImage();
        floorPlan.Image.Should().NotBeNull();

        // Act
        floorPlan.Dispose();

        // Assert
        floorPlan.Image.Should().BeNull();
    }

    [Fact]
    public void Dispose_WithNoImage_DoesNotThrow()
    {
        // Arrange
        var floorPlan = new FloorPlan();

        // Act
        Action disposeAction = () => floorPlan.Dispose();

        // Assert
        disposeAction.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var floorPlan = new FloorPlan { ImagePath = _testImagePath };
        floorPlan.LoadImage();

        // Act & Assert
        Action disposeAction = () =>
        {
            floorPlan.Dispose();
            floorPlan.Dispose();
            floorPlan.Dispose();
        };

        disposeAction.Should().NotThrow();
        floorPlan.Image.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var floorPlan = new FloorPlan();

        // Act
        floorPlan.ImagePath = "test/path.png";
        floorPlan.ImageWidth = 1024;
        floorPlan.ImageHeight = 768;
        floorPlan.MetersPerPixel = 0.025;
        floorPlan.Name = "Test Floor Plan";
        floorPlan.Notes = "Test notes";

        // Assert
        floorPlan.ImagePath.Should().Be("test/path.png");
        floorPlan.ImageWidth.Should().Be(1024);
        floorPlan.ImageHeight.Should().Be(768);
        floorPlan.MetersPerPixel.Should().Be(0.025);
        floorPlan.Name.Should().Be("Test Floor Plan");
        floorPlan.Notes.Should().Be("Test notes");
    }

    [Theory]
    [InlineData(0.01)]  // 1cm per pixel
    [InlineData(0.05)]  // 5cm per pixel (default)
    [InlineData(0.1)]   // 10cm per pixel
    [InlineData(0.5)]   // 50cm per pixel
    public void MetersPerPixel_WithVariousScales_ConvertsCorrectly(double scale)
    {
        // Arrange
        var floorPlan = new FloorPlan
        {
            ImageWidth = 1000,
            ImageHeight = 1000,
            MetersPerPixel = scale
        };

        // Act
        var (width, height) = floorPlan.DimensionsMeters;

        // Assert
        width.Should().Be(1000 * scale);
        height.Should().Be(1000 * scale);
    }

    [Fact]
    public void LoadImage_WithMultipleFormats_HandlesCorrectly()
    {
        // Arrange - Create different image formats
        var pngPath = Path.Combine(_tempDirectory, "test.png");
        var jpgPath = Path.Combine(_tempDirectory, "test.jpg");
        var bmpPath = Path.Combine(_tempDirectory, "test.bmp");

        CreateTestImage(pngPath, 100, 100);

        using (var bitmap = new Bitmap(100, 100))
        {
            bitmap.Save(jpgPath, ImageFormat.Jpeg);
            bitmap.Save(bmpPath, ImageFormat.Bmp);
        }

        // Act & Assert
        var pngPlan = new FloorPlan { ImagePath = pngPath };
        pngPlan.LoadImage().Should().BeTrue();
        pngPlan.Image.Should().NotBeNull();

        var jpgPlan = new FloorPlan { ImagePath = jpgPath };
        jpgPlan.LoadImage().Should().BeTrue();
        jpgPlan.Image.Should().NotBeNull();

        var bmpPlan = new FloorPlan { ImagePath = bmpPath };
        bmpPlan.LoadImage().Should().BeTrue();
        bmpPlan.Image.Should().NotBeNull();
    }

    // Cleanup after tests
    ~FloorPlanTests()
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
