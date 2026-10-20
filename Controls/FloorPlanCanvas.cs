using System.Drawing.Drawing2D;
using WiFiSurvey.Models;
using WiFiSurvey.Services;

namespace WiFiSurvey.Controls;

/// <summary>
/// Custom control for displaying floor plans with measurement points and heatmap overlay
/// </summary>
public class FloorPlanCanvas : Control
{
    private Image? _floorPlanImage;
    private Bitmap? _heatmapOverlay;
    private List<MeasurementPoint> _measurementPoints = new();
    private MeasurementPoint? _selectedPoint;
    private MeasurementPoint? _hoveredPoint;
    private bool _showHeatmap = true;
    private bool _showMeasurementPoints = true;
    private float _zoomLevel = 1.0f;
    private PointF _panOffset = PointF.Empty;
    private Point _lastMousePosition;
    private bool _isPanning;
    private readonly HeatmapGenerator _heatmapGenerator;

    // Point display settings
    private const int PointRadius = 8;
    private const int SelectedPointRadius = 12;
    private const int HoverPointRadius = 10;

    /// <summary>
    /// Event raised when a point on the canvas is clicked
    /// </summary>
    public event EventHandler<CanvasClickEventArgs>? CanvasClicked;

    /// <summary>
    /// Event raised when a measurement point is selected
    /// </summary>
    public event EventHandler<MeasurementPoint?>? PointSelected;

    /// <summary>
    /// Event raised when mouse hovers over a measurement point
    /// </summary>
    public event EventHandler<MeasurementPoint?>? PointHovered;

    public FloorPlanCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(40, 40, 40);
        _heatmapGenerator = new HeatmapGenerator();
    }

    #region Properties

    /// <summary>
    /// Gets or sets the floor plan image
    /// </summary>
    public Image? FloorPlanImage
    {
        get => _floorPlanImage;
        set
        {
            _floorPlanImage = value;
            ResetView();
            InvalidateHeatmap();
            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets the measurement points
    /// </summary>
    public List<MeasurementPoint> MeasurementPoints
    {
        get => _measurementPoints;
        set
        {
            _measurementPoints = value ?? new List<MeasurementPoint>();
            InvalidateHeatmap();
            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets whether to show the heatmap overlay
    /// </summary>
    public bool ShowHeatmap
    {
        get => _showHeatmap;
        set
        {
            _showHeatmap = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets whether to show measurement point markers
    /// </summary>
    public bool ShowMeasurementPoints
    {
        get => _showMeasurementPoints;
        set
        {
            _showMeasurementPoints = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets the currently selected measurement point
    /// </summary>
    public MeasurementPoint? SelectedPoint
    {
        get => _selectedPoint;
        set
        {
            _selectedPoint = value;
            PointSelected?.Invoke(this, value);
            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets the zoom level (1.0 = 100%)
    /// </summary>
    public float ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            _zoomLevel = Math.Clamp(value, 0.1f, 5.0f);
            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets the heatmap opacity (0-255)
    /// </summary>
    public int HeatmapOpacity { get; set; } = 150;

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a measurement point at the specified canvas coordinates
    /// </summary>
    public void AddMeasurementPoint(MeasurementPoint point)
    {
        _measurementPoints.Add(point);
        InvalidateHeatmap();
        Invalidate();
    }

    /// <summary>
    /// Removes a measurement point
    /// </summary>
    public bool RemoveMeasurementPoint(Guid pointId)
    {
        var point = _measurementPoints.FirstOrDefault(p => p.Id == pointId);
        if (point != null)
        {
            _measurementPoints.Remove(point);
            if (_selectedPoint?.Id == pointId)
                _selectedPoint = null;
            InvalidateHeatmap();
            Invalidate();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all measurement points
    /// </summary>
    public void ClearMeasurementPoints()
    {
        _measurementPoints.Clear();
        _selectedPoint = null;
        InvalidateHeatmap();
        Invalidate();
    }

    /// <summary>
    /// Regenerates the heatmap overlay
    /// </summary>
    public void RefreshHeatmap()
    {
        InvalidateHeatmap();
        GenerateHeatmapIfNeeded();
        Invalidate();
    }

    /// <summary>
    /// Resets the view to fit the floor plan
    /// </summary>
    public void ResetView()
    {
        _zoomLevel = 1.0f;
        _panOffset = PointF.Empty;
        FitToView();
        Invalidate();
    }

    /// <summary>
    /// Fits the floor plan to the control size
    /// </summary>
    public void FitToView()
    {
        if (_floorPlanImage == null || Width == 0 || Height == 0)
            return;

        float scaleX = (float)Width / _floorPlanImage.Width;
        float scaleY = (float)Height / _floorPlanImage.Height;
        _zoomLevel = Math.Min(scaleX, scaleY) * 0.95f; // 95% to add margin

        // Center the image
        float scaledWidth = _floorPlanImage.Width * _zoomLevel;
        float scaledHeight = _floorPlanImage.Height * _zoomLevel;
        _panOffset = new PointF((Width - scaledWidth) / 2, (Height - scaledHeight) / 2);
    }

    /// <summary>
    /// Converts screen coordinates to normalized floor plan coordinates (0-1)
    /// </summary>
    public PointF ScreenToNormalized(Point screenPoint)
    {
        if (_floorPlanImage == null)
            return PointF.Empty;

        float x = (screenPoint.X - _panOffset.X) / (_floorPlanImage.Width * _zoomLevel);
        float y = (screenPoint.Y - _panOffset.Y) / (_floorPlanImage.Height * _zoomLevel);

        return new PointF(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    /// <summary>
    /// Converts normalized floor plan coordinates to screen coordinates
    /// </summary>
    public Point NormalizedToScreen(double normalizedX, double normalizedY)
    {
        if (_floorPlanImage == null)
            return Point.Empty;

        int x = (int)(normalizedX * _floorPlanImage.Width * _zoomLevel + _panOffset.X);
        int y = (int)(normalizedY * _floorPlanImage.Height * _zoomLevel + _panOffset.Y);

        return new Point(x, y);
    }

    #endregion

    #region Protected Methods

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Draw floor plan
        if (_floorPlanImage != null)
        {
            var destRect = new RectangleF(
                _panOffset.X,
                _panOffset.Y,
                _floorPlanImage.Width * _zoomLevel,
                _floorPlanImage.Height * _zoomLevel);

            g.DrawImage(_floorPlanImage, destRect);

            // Draw heatmap overlay
            if (_showHeatmap && _measurementPoints.Count >= 3)
            {
                GenerateHeatmapIfNeeded();
                if (_heatmapOverlay != null)
                {
                    g.DrawImage(_heatmapOverlay, destRect);
                }
            }

            // Draw measurement points
            if (_showMeasurementPoints)
            {
                foreach (var point in _measurementPoints)
                {
                    DrawMeasurementPoint(g, point);
                }
            }
        }
        else
        {
            // Draw placeholder text
            using var font = new Font("Segoe UI", 14);
            using var brush = new SolidBrush(Color.Gray);
            var text = "Load a floor plan image to begin";
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            _isPanning = true;
            _lastMousePosition = e.Location;
            Cursor = Cursors.SizeAll;
        }
        else if (e.Button == MouseButtons.Left)
        {
            // Check if clicking on a point
            var clickedPoint = GetPointAtLocation(e.Location);
            if (clickedPoint != null)
            {
                SelectedPoint = clickedPoint;
            }
            else if (_floorPlanImage != null)
            {
                var normalized = ScreenToNormalized(e.Location);
                if (normalized.X >= 0 && normalized.X <= 1 && normalized.Y >= 0 && normalized.Y <= 1)
                {
                    CanvasClicked?.Invoke(this, new CanvasClickEventArgs(normalized.X, normalized.Y, e.Location));
                }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPanning)
        {
            _panOffset.X += e.X - _lastMousePosition.X;
            _panOffset.Y += e.Y - _lastMousePosition.Y;
            _lastMousePosition = e.Location;
            Invalidate();
        }
        else
        {
            // Check for hover
            var hoveredPoint = GetPointAtLocation(e.Location);
            if (hoveredPoint != _hoveredPoint)
            {
                _hoveredPoint = hoveredPoint;
                PointHovered?.Invoke(this, hoveredPoint);
                Invalidate();
            }

            Cursor = hoveredPoint != null ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Right)
        {
            _isPanning = false;
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        // Zoom toward mouse position
        var mousePos = e.Location;
        var normalizedBefore = ScreenToNormalized(mousePos);

        float zoomDelta = e.Delta > 0 ? 1.1f : 0.9f;
        ZoomLevel *= zoomDelta;

        // Adjust pan to keep mouse position stable
        if (_floorPlanImage != null)
        {
            var newScreenPos = NormalizedToScreen(normalizedBefore.X, normalizedBefore.Y);
            _panOffset.X += mousePos.X - newScreenPos.X;
            _panOffset.Y += mousePos.Y - newScreenPos.Y;
        }

        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateHeatmap();
        Invalidate();
    }

    #endregion

    #region Private Methods

    private void DrawMeasurementPoint(Graphics g, MeasurementPoint point)
    {
        var screenPos = NormalizedToScreen(point.X, point.Y);
        var color = point.GetSignalColor();

        int radius = PointRadius;
        if (point == _selectedPoint)
            radius = SelectedPointRadius;
        else if (point == _hoveredPoint)
            radius = HoverPointRadius;

        // Draw outer ring
        using (var brush = new SolidBrush(Color.FromArgb(200, color)))
        {
            g.FillEllipse(brush, screenPos.X - radius, screenPos.Y - radius, radius * 2, radius * 2);
        }

        // Draw border
        using (var pen = new Pen(Color.White, 2))
        {
            g.DrawEllipse(pen, screenPos.X - radius, screenPos.Y - radius, radius * 2, radius * 2);
        }

        // Draw signal value for selected point
        if (point == _selectedPoint || point == _hoveredPoint)
        {
            using var font = new Font("Segoe UI", 9, FontStyle.Bold);
            var text = $"{point.SignalStrength} dBm";
            var size = g.MeasureString(text, font);

            var labelRect = new RectangleF(
                screenPos.X - size.Width / 2 - 2,
                screenPos.Y - radius - size.Height - 5,
                size.Width + 4,
                size.Height + 2);

            using (var brush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            {
                g.FillRectangle(brush, labelRect);
            }
            g.DrawString(text, font, Brushes.White, labelRect.X + 2, labelRect.Y + 1);
        }
    }

    private MeasurementPoint? GetPointAtLocation(Point screenLocation)
    {
        const int hitRadius = 15;

        foreach (var point in _measurementPoints)
        {
            var screenPos = NormalizedToScreen(point.X, point.Y);
            var distance = Math.Sqrt(Math.Pow(screenLocation.X - screenPos.X, 2) +
                                    Math.Pow(screenLocation.Y - screenPos.Y, 2));

            if (distance <= hitRadius)
                return point;
        }

        return null;
    }

    private void InvalidateHeatmap()
    {
        _heatmapOverlay?.Dispose();
        _heatmapOverlay = null;
    }

    private void GenerateHeatmapIfNeeded()
    {
        if (_heatmapOverlay != null || _floorPlanImage == null || _measurementPoints.Count < 3)
            return;

        int width = (int)(_floorPlanImage.Width * _zoomLevel);
        int height = (int)(_floorPlanImage.Height * _zoomLevel);

        if (width > 0 && height > 0)
        {
            _heatmapOverlay = _heatmapGenerator.GeneratePreviewHeatmap(
                width, height, _measurementPoints, 4);
        }
    }

    #endregion
}

/// <summary>
/// Event arguments for canvas click events
/// </summary>
public class CanvasClickEventArgs : EventArgs
{
    public double NormalizedX { get; }
    public double NormalizedY { get; }
    public Point ScreenLocation { get; }

    public CanvasClickEventArgs(double normalizedX, double normalizedY, Point screenLocation)
    {
        NormalizedX = normalizedX;
        NormalizedY = normalizedY;
        ScreenLocation = screenLocation;
    }
}
