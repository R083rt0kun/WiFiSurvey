using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WiFiSurvey.Models;

namespace WiFiSurvey.Services;

/// <summary>
/// Generates heatmap overlays from Wi-Fi measurement data
/// </summary>
public class HeatmapGenerator
{
    private readonly int _interpolationRadius;
    private readonly double _smoothingFactor;

    /// <summary>
    /// Creates a new heatmap generator
    /// </summary>
    /// <param name="interpolationRadius">Radius for interpolation in pixels</param>
    /// <param name="smoothingFactor">Smoothing factor (0-1, higher = smoother)</param>
    public HeatmapGenerator(int interpolationRadius = 100, double smoothingFactor = 0.7)
    {
        _interpolationRadius = interpolationRadius;
        _smoothingFactor = Math.Clamp(smoothingFactor, 0, 1);
    }

    /// <summary>
    /// Generates a heatmap overlay from measurement points
    /// </summary>
    /// <param name="width">Width of the output image</param>
    /// <param name="height">Height of the output image</param>
    /// <param name="measurements">Measurement points</param>
    /// <param name="opacity">Overlay opacity (0-255)</param>
    /// <returns>Bitmap with heatmap overlay</returns>
    public Bitmap GenerateHeatmap(int width, int height, List<MeasurementPoint> measurements, int opacity = 150)
    {
        // Handle invalid dimensions gracefully
        if (width <= 0 || height <= 0)
        {
            return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        if (measurements.Count == 0)
            return bitmap;

        // Create signal strength grid using IDW interpolation
        var signalGrid = new double[width, height];
        var weightGrid = new double[width, height];

        // Convert normalized coordinates to pixel coordinates
        var pixelPoints = measurements.Select(m => new
        {
            X = (int)(m.X * width),
            Y = (int)(m.Y * height),
            Signal = m.SignalStrength
        }).ToList();

        // Calculate interpolated values for each pixel
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double totalWeight = 0;
                double weightedSignal = 0;

                foreach (var point in pixelPoints)
                {
                    double distance = Math.Sqrt(Math.Pow(x - point.X, 2) + Math.Pow(y - point.Y, 2));

                    if (distance < 1) distance = 1; // Avoid division by zero

                    // Inverse Distance Weighting
                    double weight = 1.0 / Math.Pow(distance, 2);

                    // Apply radius falloff
                    if (distance > _interpolationRadius)
                    {
                        weight *= Math.Exp(-((distance - _interpolationRadius) / (_interpolationRadius * _smoothingFactor)));
                    }

                    weightedSignal += point.Signal * weight;
                    totalWeight += weight;
                }

                if (totalWeight > 0)
                {
                    signalGrid[x, y] = weightedSignal / totalWeight;
                    weightGrid[x, y] = totalWeight;
                }
                else
                {
                    signalGrid[x, y] = -100; // Default weak signal
                }
            }
        }

        // Apply Gaussian blur for smoother appearance
        signalGrid = ApplyGaussianBlur(signalGrid, width, height, 5);

        // Convert signal grid to colors using LockBits for performance
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = bitmapData.Stride;
            int bytesPerPixel = 4; // 32bpp = 4 bytes per pixel
            byte[] pixels = new byte[stride * height];

            // Populate pixel array
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var color = SignalToColor(signalGrid[x, y], opacity);
                    int offset = y * stride + x * bytesPerPixel;

                    // Format32bppArgb uses BGRA byte order
                    pixels[offset] = color.B;       // Blue
                    pixels[offset + 1] = color.G;   // Green
                    pixels[offset + 2] = color.R;   // Red
                    pixels[offset + 3] = color.A;   // Alpha
                }
            }

            // Copy pixel data to bitmap
            System.Runtime.InteropServices.Marshal.Copy(
                pixels,
                0,
                bitmapData.Scan0,
                pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    /// <summary>
    /// Generates a fast preview heatmap (lower resolution for performance)
    /// </summary>
    public Bitmap GeneratePreviewHeatmap(int width, int height, List<MeasurementPoint> measurements, int scale = 4)
    {
        int previewWidth = width / scale;
        int previewHeight = height / scale;

        var preview = GenerateHeatmap(previewWidth, previewHeight, measurements, 150);

        // Scale up
        var scaled = new Bitmap(width, height);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(preview, 0, 0, width, height);
        }

        preview.Dispose();
        return scaled;
    }

    /// <summary>
    /// Converts signal strength to a color
    /// </summary>
    private Color SignalToColor(double signalStrength, int opacity)
    {
        // Normalize signal strength (-30 to -90 dBm typical range)
        double normalized = Math.Clamp((signalStrength + 90) / 60.0, 0, 1);

        // Create color gradient: Red -> Yellow -> Green
        int r, g, b;

        if (normalized < 0.5)
        {
            // Red to Yellow
            r = 255;
            g = (int)(normalized * 2 * 255);
            b = 0;
        }
        else
        {
            // Yellow to Green
            r = (int)((1 - (normalized - 0.5) * 2) * 255);
            g = 255;
            b = 0;
        }

        return Color.FromArgb(opacity, r, g, b);
    }

    /// <summary>
    /// Applies Gaussian blur to the signal grid
    /// </summary>
    private double[,] ApplyGaussianBlur(double[,] grid, int width, int height, int kernelSize)
    {
        var result = new double[width, height];
        var kernel = CreateGaussianKernel(kernelSize);
        int radius = kernelSize / 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                double weightSum = 0;

                for (int ky = -radius; ky <= radius; ky++)
                {
                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        int nx = x + kx;
                        int ny = y + ky;

                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            double weight = kernel[kx + radius, ky + radius];
                            sum += grid[nx, ny] * weight;
                            weightSum += weight;
                        }
                    }
                }

                result[x, y] = weightSum > 0 ? sum / weightSum : grid[x, y];
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a Gaussian kernel for blurring
    /// </summary>
    private double[,] CreateGaussianKernel(int size)
    {
        var kernel = new double[size, size];
        int radius = size / 2;
        double sigma = size / 3.0;
        double twoSigmaSquare = 2 * sigma * sigma;
        double sum = 0;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                double value = Math.Exp(-(x * x + y * y) / twoSigmaSquare);
                kernel[x + radius, y + radius] = value;
                sum += value;
            }
        }

        // Normalize
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                kernel[x, y] /= sum;
            }
        }

        return kernel;
    }

    /// <summary>
    /// Creates a legend image for the heatmap
    /// </summary>
    public Bitmap CreateLegend(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe UI", 9);
        using var brush = new SolidBrush(Color.Black);

        // Draw gradient bar
        int barWidth = width - 80;
        int barHeight = 20;
        int barX = 10;
        int barY = 10;

        for (int x = 0; x < barWidth; x++)
        {
            double signal = -90 + (x / (double)barWidth) * 60; // -90 to -30
            var color = SignalToColor(signal, 255);
            using var pen = new Pen(color);
            g.DrawLine(pen, barX + x, barY, barX + x, barY + barHeight);
        }

        // Draw border
        g.DrawRectangle(Pens.Black, barX, barY, barWidth, barHeight);

        // Draw labels
        g.DrawString("-90 dBm", font, brush, barX - 5, barY + barHeight + 5);
        g.DrawString("-60 dBm", font, brush, barX + barWidth / 2 - 20, barY + barHeight + 5);
        g.DrawString("-30 dBm", font, brush, barX + barWidth - 25, barY + barHeight + 5);

        // Draw quality labels
        int labelY = barY + barHeight + 25;
        g.FillRectangle(new SolidBrush(SignalToColor(-40, 255)), 10, labelY, 15, 15);
        g.DrawString("Excellent", font, brush, 30, labelY);

        g.FillRectangle(new SolidBrush(SignalToColor(-55, 255)), 100, labelY, 15, 15);
        g.DrawString("Good", font, brush, 120, labelY);

        g.FillRectangle(new SolidBrush(SignalToColor(-65, 255)), 170, labelY, 15, 15);
        g.DrawString("Fair", font, brush, 190, labelY);

        g.FillRectangle(new SolidBrush(SignalToColor(-75, 255)), 230, labelY, 15, 15);
        g.DrawString("Weak", font, brush, 250, labelY);

        g.FillRectangle(new SolidBrush(SignalToColor(-85, 255)), 300, labelY, 15, 15);
        g.DrawString("Poor", font, brush, 320, labelY);

        return bitmap;
    }
}
