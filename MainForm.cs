using System.Text;
using WifiSurvey.Controls;
using WifiSurvey.Models;
using WifiSurvey.Services;

namespace WifiSurvey;

/// <summary>
/// Main application form for WiFi Survey Tool
/// Modern tabbed interface with Live Monitor and Site Survey views
/// </summary>
public partial class MainForm : Form
{
    private SurveyProject _project = new();
    private WifiScanner? _wifiScanner;
    private readonly HeatmapGenerator _heatmapGenerator = new();

    // Main UI Controls
    private MenuStrip _menuStrip = null!;
    private ToolStrip _toolStrip = null!;
    private StatusStrip _statusStrip = null!;
    private TabControl _mainTabControl = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _wifiStatusLabel = null!;
    private ToolStripProgressBar _progressBar = null!;

    // Live Monitor Tab Controls
    private TabPage _liveMonitorTab = null!;
    private Panel _signalGraphPanel = null!;
    private Panel _connectionDetailsPanel = null!;
    private ListView _networkListView = null!;
    private System.Windows.Forms.Timer _monitorTimer = null!;
    private const int ScanIntervalSeconds = 20;
    private DateTime _lastScanStartUtc = DateTime.MinValue;
    private bool _scanInProgress;
    private bool _measurementInProgress;
    private string _scanStatusText = "Scan pending";
    private DateTime? _lastBssHistorySample;
    private Label _signalValueLabel = null!;
    private Label _signalQualityLabel = null!;
    private Label _connectedSsidLabel = null!;
    private Label _channelLabel = null!;
    private Label _bandLabel = null!;
    private Label _bssidLabel = null!;
    private Label _speedLabel = null!;
    private CheckBox _autoRefreshCheckbox = null!;
    private TrackBar _refreshRateSlider = null!;
    private List<int> _signalHistory = new();
    private const int MaxSignalHistoryPoints = 60;
    private string? _connectedBssid;
    private int _networkListSortColumn = -1;
    private SortOrder _networkListSortOrder = SortOrder.None;

    // Site Survey Tab Controls
    private TabPage _siteSurveyTab = null!;
    private SplitContainer _surveySplitContainer = null!;
    private FloorPlanCanvas _canvas = null!;
    private Panel _sidePanel = null!;
    private ListView _pointsListView = null!;
    private PropertyGrid _propertyGrid = null!;
    private Label _statsLabel = null!;

    public MainForm()
    {
        InitializeComponent();
        InitializeWifiScanner();
        Shown += OnMainFormShown;
    }

    private void OnMainFormShown(object? sender, EventArgs e)
    {
        if (_mainTabControl.SelectedTab != _liveMonitorTab || _wifiScanner == null)
            return;

        RefreshLiveMonitor();
        _ = ScanAndRefreshAsync(force: true);
        if (_autoRefreshCheckbox.Checked)
            _monitorTimer.Start();
    }

    private void InitializeComponent()
    {
        Text = "WiFi Survey Tool - CosmicBytez";
        Size = new Size(1400, 900);
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        // Menu Strip
        _menuStrip = new MenuStrip();
        CreateMenus();

        // Tool Strip
        _toolStrip = new ToolStrip();
        CreateToolbar();

        // Status Strip
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _wifiStatusLabel = new ToolStripStatusLabel("WiFi: Initializing...");
        _progressBar = new ToolStripProgressBar { Visible = false, Width = 150 };
        _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _progressBar, _wifiStatusLabel });

        // Main Tab Control
        _mainTabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10)
        };

        // Create tabs
        CreateLiveMonitorTab();
        CreateSiteSurveyTab();

        _mainTabControl.TabPages.Add(_liveMonitorTab);
        _mainTabControl.TabPages.Add(_siteSurveyTab);
        _mainTabControl.SelectedIndexChanged += OnTabChanged;

        // Add controls
        Controls.Add(_mainTabControl);
        Controls.Add(_toolStrip);
        Controls.Add(_menuStrip);
        Controls.Add(_statusStrip);

        MainMenuStrip = _menuStrip;

        // Initialize timer
        _monitorTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _monitorTimer.Tick += OnMonitorTimerTick;
    }

    #region Live Monitor Tab

    private void CreateLiveMonitorTab()
    {
        _liveMonitorTab = new TabPage("Live Monitor")
        {
            Padding = new Padding(10)
        };

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        // Left panel - Signal Graph + Connection Details
        var leftPanel = new Panel { Dock = DockStyle.Fill };

        // Signal Graph Panel (top)
        _signalGraphPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 250,
            BackColor = Color.FromArgb(30, 30, 40),
            Padding = new Padding(10)
        };
        _signalGraphPanel.Paint += OnSignalGraphPaint;

        var graphTitle = new Label
        {
            Text = "Signal Strength Over Time",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Color.Transparent
        };
        _signalGraphPanel.Controls.Add(graphTitle);

        // Connection Details Panel (bottom of left)
        _connectionDetailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 245, 250),
            Padding = new Padding(15)
        };
        CreateConnectionDetailsPanel();

        leftPanel.Controls.Add(_connectionDetailsPanel);
        leftPanel.Controls.Add(_signalGraphPanel);

        // Right panel - Network List
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var networkTitle = new Label
        {
            Text = "Visible Networks",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30
        };

        _networkListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9)
        };
        _networkListView.Columns.Add("SSID", 150);
        _networkListView.Columns.Add("BSSID", 155);
        _networkListView.Columns.Add("Signal", 70);
        _networkListView.Columns.Add("Quality", 60);
        _networkListView.Columns.Add("Channel", 60);
        _networkListView.Columns.Add("Band", 70);
        _networkListView.ColumnClick += OnNetworkListColumnClick;

        // Control panel at bottom
        var controlPanel = new Panel { Dock = DockStyle.Bottom, Height = 60 };

        _autoRefreshCheckbox = new CheckBox
        {
            Text = "Auto-refresh",
            Checked = true,
            Location = new Point(10, 5),
            AutoSize = true
        };
        _autoRefreshCheckbox.CheckedChanged += OnAutoRefreshChanged;

        var refreshRateLabel = new Label
        {
            Text = "Refresh rate:",
            Location = new Point(10, 32),
            AutoSize = true
        };

        _refreshRateSlider = new TrackBar
        {
            Minimum = 1,
            Maximum = 10,
            Value = 2,
            TickFrequency = 1,
            Location = new Point(90, 28),
            Width = 120,
            Height = 25
        };
        _refreshRateSlider.ValueChanged += OnRefreshRateChanged;

        var refreshRateValueLabel = new Label
        {
            Text = "2s",
            Location = new Point(215, 32),
            AutoSize = true
        };
        _refreshRateSlider.Tag = refreshRateValueLabel;

        var manualRefreshBtn = new Button
        {
            Text = "Refresh Now",
            Location = new Point(260, 15),
            Width = 100
        };
        manualRefreshBtn.Click += async (s, e) => await ScanAndRefreshAsync(force: true);

        controlPanel.Controls.AddRange(new Control[] {
            _autoRefreshCheckbox, refreshRateLabel, _refreshRateSlider,
            refreshRateValueLabel, manualRefreshBtn
        });

        rightPanel.Controls.Add(_networkListView);
        rightPanel.Controls.Add(networkTitle);
        rightPanel.Controls.Add(controlPanel);

        splitContainer.Panel1.Controls.Add(leftPanel);
        splitContainer.Panel2.Controls.Add(rightPanel);

        _liveMonitorTab.Controls.Add(splitContainer);

    }

    private void CreateConnectionDetailsPanel()
    {
        var title = new Label
        {
            Text = "Current Connection",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 50, 60),
            Dock = DockStyle.Top,
            Height = 40
        };

        // Main signal display
        var signalPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            Padding = new Padding(10)
        };

        _signalValueLabel = new Label
        {
            Text = "-- dBm",
            Font = new Font("Segoe UI", 36, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 200),
            Location = new Point(10, 10),
            AutoSize = true
        };

        _signalQualityLabel = new Label
        {
            Text = "No Connection",
            Font = new Font("Segoe UI", 14),
            ForeColor = Color.Gray,
            Location = new Point(200, 30),
            AutoSize = true
        };

        signalPanel.Controls.Add(_signalValueLabel);
        signalPanel.Controls.Add(_signalQualityLabel);

        // Details grid
        var detailsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 180,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(10)
        };
        detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

        var labelFont = new Font("Segoe UI", 10);
        var valueFont = new Font("Segoe UI", 10, FontStyle.Bold);

        AddDetailRow(detailsPanel, 0, "SSID:", out _connectedSsidLabel, labelFont, valueFont);
        AddDetailRow(detailsPanel, 1, "Channel:", out _channelLabel, labelFont, valueFont);
        AddDetailRow(detailsPanel, 2, "Band:", out _bandLabel, labelFont, valueFont);
        AddDetailRow(detailsPanel, 3, "BSSID:", out _bssidLabel, labelFont, valueFont);
        AddDetailRow(detailsPanel, 4, "Max Rate:", out _speedLabel, labelFont, valueFont);

        _connectionDetailsPanel.Controls.Add(detailsPanel);
        _connectionDetailsPanel.Controls.Add(signalPanel);
        _connectionDetailsPanel.Controls.Add(title);
    }

    private void AddDetailRow(TableLayoutPanel panel, int row, string labelText, out Label valueLabel, Font labelFont, Font valueFont)
    {
        var label = new Label
        {
            Text = labelText,
            Font = labelFont,
            ForeColor = Color.Gray,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        valueLabel = new Label
        {
            Text = "--",
            Font = valueFont,
            ForeColor = Color.FromArgb(50, 50, 60),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(valueLabel, 1, row);
    }

    private void OnSignalGraphPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var rect = _signalGraphPanel.ClientRectangle;
        rect.Y += 35; // Below title
        rect.Height -= 45;
        rect.X += 50; // Left margin for axis
        rect.Width -= 60;

        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Draw grid lines
        using var gridPen = new Pen(Color.FromArgb(60, 60, 70), 1);
        using var axisPen = new Pen(Color.FromArgb(100, 100, 110), 1);
        using var textBrush = new SolidBrush(Color.FromArgb(150, 150, 160));
        using var axisFont = new Font("Segoe UI", 8);

        // Horizontal grid lines (signal levels)
        int[] levels = { -30, -50, -70, -90 };
        foreach (var level in levels)
        {
            float y = rect.Y + ((-30 - level) / 70f) * rect.Height;
            g.DrawLine(gridPen, rect.X, y, rect.Right, y);
            g.DrawString($"{level}", axisFont, textBrush, 5, y - 6);
        }

        // Draw signal line
        if (_signalHistory.Count > 1)
        {
            var points = new PointF[_signalHistory.Count];
            for (int i = 0; i < _signalHistory.Count; i++)
            {
                float x = rect.X + (i / (float)(MaxSignalHistoryPoints - 1)) * rect.Width;
                float signal = Math.Max(-100, Math.Min(-30, _signalHistory[i]));
                float y = rect.Y + ((-30 - signal) / 70f) * rect.Height;
                points[i] = new PointF(x, y);
            }

            // Fill under curve
            using var fillBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Point(0, rect.Y), new Point(0, rect.Bottom),
                Color.FromArgb(80, 0, 200, 100),
                Color.FromArgb(10, 0, 200, 100));

            var fillPoints = new PointF[points.Length + 2];
            Array.Copy(points, fillPoints, points.Length);
            fillPoints[points.Length] = new PointF(points[^1].X, rect.Bottom);
            fillPoints[points.Length + 1] = new PointF(points[0].X, rect.Bottom);
            g.FillPolygon(fillBrush, fillPoints);

            // Draw line
            using var linePen = new Pen(Color.FromArgb(0, 200, 100), 2);
            g.DrawLines(linePen, points);

            // Draw current value dot
            if (points.Length > 0)
            {
                var lastPoint = points[^1];
                g.FillEllipse(Brushes.White, lastPoint.X - 5, lastPoint.Y - 5, 10, 10);
                g.FillEllipse(Brushes.LimeGreen, lastPoint.X - 3, lastPoint.Y - 3, 6, 6);
            }
        }

        // Draw axes
        g.DrawLine(axisPen, rect.X, rect.Y, rect.X, rect.Bottom);
        g.DrawLine(axisPen, rect.X, rect.Bottom, rect.Right, rect.Bottom);

        // X-axis label
        g.DrawString("Time (60s)", axisFont, textBrush, rect.Right - 50, rect.Bottom + 5);
    }

    // A full RF scan is costly, so refresh the BSS cache frequently but
    // initiate physical scans only at a controlled interval or on demand.
    private async Task ScanAndRefreshAsync(bool force = false)
    {
        if (_wifiScanner == null || _scanInProgress || IsDisposed || Disposing)
            return;
        if (!force && (DateTime.UtcNow - _lastScanStartUtc).TotalSeconds < ScanIntervalSeconds)
            return;

        _scanInProgress = true;
        _lastScanStartUtc = DateTime.UtcNow;
        _scanStatusText = "Scanning...";
        _statusLabel.Text = _scanStatusText;
        try
        {
            var result = await _wifiScanner.ScanAsync();
            if (IsDisposed || Disposing) return;
            _scanStatusText = result.Succeeded
                ? $"{result.Description} at {result.Timestamp:HH:mm:ss}"
                : $"{result.Description}";
            RefreshLiveMonitor();
        }
        catch (OperationCanceledException) { /* Form is closing. */ }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                _scanStatusText = $"Scan error: {ex.Message}";
                _statusLabel.Text = _scanStatusText;
            }
        }
        finally
        {
            _scanInProgress = false;
        }
    }

    private void RefreshLiveMonitor()
    {
        if (_wifiScanner == null) return;

        try
        {
            var networks = _wifiScanner.GetAvailableNetworks();
            bool showingOldResults = _wifiScanner.LastReadUsedOldResults;
            // Resolve the actual Windows connection by BSSID instead of
            // assuming that the strongest detected AP is the connected one.
            var connectedBssid = _wifiScanner.GetConnectedBssid();
            var connected = networks.FirstOrDefault(n =>
                string.Equals(n.BSSID, connectedBssid, StringComparison.OrdinalIgnoreCase));

            // Update connection details
            if (connected != null)
            {
                // Real-time RSSI comes from WlanQueryInterface, not the BSS scan cache.
                int? liveRssi = _wifiScanner.GetConnectedRssi();
                int signal = liveRssi ?? connected.SignalStrength;
                _signalValueLabel.Text = $"{signal} dBm";
                _signalValueLabel.ForeColor = GetSignalColor(signal);
                _signalQualityLabel.Text = connected.SignalQualityDescription;
                _signalQualityLabel.ForeColor = GetSignalColor(connected.SignalStrength);
                _connectedSsidLabel.Text = connected.SSID;
                _channelLabel.Text = connected.Channel.ToString();
                _bandLabel.Text = connected.Band;
                _bssidLabel.Text = connected.BSSID;
                _speedLabel.Text = connected.MaxRate > 0 ? $"{connected.MaxRate:F0} Mbps" : "--";

                // A cached BSS RSSI is not a new measurement every 2 seconds.
                // If live RSSI isn't available, add one point only after a new scan.
                bool newBssObservation = !showingOldResults &&
                    _wifiScanner.LastCompletedScan is DateTime completedAt &&
                    _lastBssHistorySample != completedAt;
                if (liveRssi.HasValue || newBssObservation)
                {
                    _signalHistory.Add(signal);
                    if (_signalHistory.Count > MaxSignalHistoryPoints)
                        _signalHistory.RemoveAt(0);
                    if (newBssObservation)
                        _lastBssHistorySample = _wifiScanner.LastCompletedScan;
                }
            }
            else
            {
                _signalValueLabel.Text = "-- dBm";
                _signalValueLabel.ForeColor = Color.Gray;
                // A connected BSSID may be absent from an incomplete scan.
                // In that case, do not report a different AP or invent RSSI.
                _signalQualityLabel.Text = !string.IsNullOrEmpty(connectedBssid)
                    ? "Connected AP not in scan" : "No Connection";
                _signalQualityLabel.ForeColor = Color.Gray;
                _connectedSsidLabel.Text = "--";
                _channelLabel.Text = "--";
                _bandLabel.Text = "--";
                _bssidLabel.Text = connectedBssid ?? "--";
                _speedLabel.Text = "--";
            }

            // Update network list
            _networkListView.BeginUpdate();
            _networkListView.Items.Clear();
            foreach (var network in networks.OrderByDescending(n => n.SignalStrength))
            {
                var ssid = string.IsNullOrEmpty(network.SSID) ? "(Hidden)" : network.SSID;
                var item = new ListViewItem(ssid);
                item.SubItems.Add(network.BSSID);
                item.SubItems.Add($"{network.SignalStrength} dBm");
                item.SubItems.Add($"{network.LinkQuality}%");
                item.SubItems.Add(network.Channel.ToString());
                item.SubItems.Add(network.Band);
                item.Tag = network.BSSID;

                // Highlight connected network
                if (connected != null &&
                    string.Equals(network.BSSID, connected.BSSID, StringComparison.OrdinalIgnoreCase))
                {
                    item.Font = new Font(_networkListView.Font, FontStyle.Bold);
                    item.BackColor = Color.FromArgb(230, 255, 230);
                    item.Text = $"\u2713 {ssid}";  // Checkmark prefix
                    _connectedBssid = network.BSSID;
                }
                else
                {
                    item.ForeColor = GetSignalColor(network.SignalStrength);
                }
                _networkListView.Items.Add(item);
            }
            _networkListView.EndUpdate();

            // Refresh graph
            _signalGraphPanel.Invalidate();

            // A BSS entry represents a radio-network instance, not necessarily
            // a separate physical access point or a distinct SSID.
            int bssidCount = networks
                .Where(n => !string.IsNullOrWhiteSpace(n.BSSID))
                .Select(n => n.BSSID)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            string apStatus = connected == null && !string.IsNullOrEmpty(connectedBssid)
                ? $"{bssidCount} BSSIDs; connected BSSID not in scan"
                : $"{bssidCount} BSSIDs detected";
            _statusLabel.Text = $"Live Monitor: {apStatus}{(showingOldResults ? " (STALE LIST)" : "")} | {_scanStatusText}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Scan error: {ex.Message}";
        }
    }

    private Color GetSignalColor(int signalStrength)
    {
        return signalStrength switch
        {
            >= -50 => Color.FromArgb(0, 150, 0),    // Excellent - Green
            >= -60 => Color.FromArgb(100, 150, 0),  // Good - Yellow-Green
            >= -70 => Color.FromArgb(200, 150, 0),  // Fair - Orange
            >= -80 => Color.FromArgb(200, 100, 0),  // Weak - Dark Orange
            _ => Color.FromArgb(200, 0, 0)          // Poor - Red
        };
    }

    private void OnMonitorTimerTick(object? sender, EventArgs e)
    {
        if (_mainTabControl.SelectedTab == _liveMonitorTab)
        {
            RefreshLiveMonitor();
            _ = ScanAndRefreshAsync();
        }
    }

    private void OnAutoRefreshChanged(object? sender, EventArgs e)
    {
        if (_autoRefreshCheckbox.Checked)
        {
            _monitorTimer.Start();
        }
        else
        {
            _monitorTimer.Stop();
        }
    }

    private void OnRefreshRateChanged(object? sender, EventArgs e)
    {
        _monitorTimer.Interval = _refreshRateSlider.Value * 1000;
        if (_refreshRateSlider.Tag is Label label)
        {
            label.Text = $"{_refreshRateSlider.Value}s";
        }
    }

    private void OnNetworkListColumnClick(object? sender, ColumnClickEventArgs e)
    {
        // Toggle sort order if same column, otherwise sort ascending
        if (e.Column == _networkListSortColumn)
        {
            _networkListSortOrder = _networkListSortOrder == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
        }
        else
        {
            _networkListSortColumn = e.Column;
            _networkListSortOrder = SortOrder.Ascending;
        }

        _networkListView.ListViewItemSorter = new ListViewItemComparer(_networkListSortColumn, _networkListSortOrder);
        _networkListView.Sort();
    }

    private void OnTabChanged(object? sender, EventArgs e)
    {
        if (_mainTabControl.SelectedTab == _liveMonitorTab)
        {
            RefreshLiveMonitor();
            _ = ScanAndRefreshAsync();
            if (_autoRefreshCheckbox.Checked)
            {
                _monitorTimer.Start();
            }
            _statusLabel.Text = "Live Monitor active";
        }
        else
        {
            _monitorTimer.Stop();
            _statusLabel.Text = "Site Survey mode";
        }
    }

    #endregion

    #region Site Survey Tab

    private void CreateSiteSurveyTab()
    {
        _siteSurveyTab = new TabPage("Site Survey")
        {
            Padding = new Padding(5)
        };

        _surveySplitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        // Canvas
        _canvas = new FloorPlanCanvas { Dock = DockStyle.Fill };
        _canvas.CanvasClicked += OnCanvasClicked;
        _canvas.PointSelected += OnPointSelected;
        _canvas.PointHovered += OnPointHovered;
        _surveySplitContainer.Panel1.Controls.Add(_canvas);

        // Side Panel
        _sidePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        CreateSidePanel();
        _surveySplitContainer.Panel2.Controls.Add(_sidePanel);

        _siteSurveyTab.Controls.Add(_surveySplitContainer);

    }

    private void CreateSidePanel()
    {
        // Title
        var titleLabel = new Label
        {
            Text = "Measurement Points",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30
        };

        // Points List
        _pointsListView = new ListView
        {
            Dock = DockStyle.Top,
            Height = 200,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _pointsListView.Columns.Add("SSID", 100);
        _pointsListView.Columns.Add("Signal", 60);
        _pointsListView.Columns.Add("Channel", 50);
        _pointsListView.Columns.Add("Quality", 60);
        _pointsListView.SelectedIndexChanged += OnListViewSelectionChanged;

        // Delete button
        var deleteBtn = new Button
        {
            Text = "Delete Selected",
            Dock = DockStyle.Top,
            Height = 30
        };
        deleteBtn.Click += (s, e) => DeleteSelectedPoint();

        // Property Grid
        var propLabel = new Label
        {
            Text = "Point Details",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 25
        };

        _propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Top,
            Height = 250,
            PropertySort = PropertySort.Categorized
        };

        // Statistics
        var statsTitle = new Label
        {
            Text = "Statistics",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 25
        };

        _statsLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("Consolas", 9)
        };

        // Add to panel (reverse order for docking)
        _sidePanel.Controls.Add(_statsLabel);
        _sidePanel.Controls.Add(statsTitle);
        _sidePanel.Controls.Add(_propertyGrid);
        _sidePanel.Controls.Add(propLabel);
        _sidePanel.Controls.Add(deleteBtn);
        _sidePanel.Controls.Add(_pointsListView);
        _sidePanel.Controls.Add(titleLabel);
    }

    #endregion

    #region Menus and Toolbar

    private void CreateMenus()
    {
        // File Menu
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add("&New Project", null, (s, e) => NewProject());
        fileMenu.DropDownItems.Add("&Open Project...", null, (s, e) => OpenProject());
        fileMenu.DropDownItems.Add("&Save Project", null, (s, e) => SaveProject());
        fileMenu.DropDownItems.Add("Save Project &As...", null, (s, e) => SaveProjectAs());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("&Load Floor Plan...", null, (s, e) => LoadFloorPlan());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("E&xit", null, (s, e) => Close());

        // View Menu
        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add("&Live Monitor", null, (s, e) => _mainTabControl.SelectedTab = _liveMonitorTab);
        viewMenu.DropDownItems.Add("&Site Survey", null, (s, e) => _mainTabControl.SelectedTab = _siteSurveyTab);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        var showHeatmap = new ToolStripMenuItem("Show &Heatmap") { Checked = true, CheckOnClick = true };
        showHeatmap.CheckedChanged += (s, e) => { _canvas.ShowHeatmap = showHeatmap.Checked; };
        var showPoints = new ToolStripMenuItem("Show &Points") { Checked = true, CheckOnClick = true };
        showPoints.CheckedChanged += (s, e) => { _canvas.ShowMeasurementPoints = showPoints.Checked; };
        viewMenu.DropDownItems.Add(showHeatmap);
        viewMenu.DropDownItems.Add(showPoints);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add("&Reset View", null, (s, e) => _canvas.ResetView());
        viewMenu.DropDownItems.Add("&Fit to Window", null, (s, e) => _canvas.FitToView());

        // Monitor Menu
        var monitorMenu = new ToolStripMenuItem("&Monitor");
        monitorMenu.DropDownItems.Add("&Refresh Now", null,
            async (s, e) => await ScanAndRefreshAsync(force: true));
        monitorMenu.DropDownItems.Add(new ToolStripSeparator());
        var startMonitor = new ToolStripMenuItem("&Start Auto-Refresh");
        startMonitor.Click += (s, e) => { _autoRefreshCheckbox.Checked = true; _monitorTimer.Start(); };
        var stopMonitor = new ToolStripMenuItem("S&top Auto-Refresh");
        stopMonitor.Click += (s, e) => { _autoRefreshCheckbox.Checked = false; _monitorTimer.Stop(); };
        monitorMenu.DropDownItems.Add(startMonitor);
        monitorMenu.DropDownItems.Add(stopMonitor);
        monitorMenu.DropDownItems.Add(new ToolStripSeparator());
        monitorMenu.DropDownItems.Add("&Clear History", null, (s, e) => { _signalHistory.Clear(); _signalGraphPanel.Invalidate(); });

        // Survey Menu
        var surveyMenu = new ToolStripMenuItem("&Survey");
        surveyMenu.DropDownItems.Add("&Take Measurement", null, (s, e) => TakeMeasurementAtCenter());
        surveyMenu.DropDownItems.Add("&Clear All Points", null, (s, e) => ClearAllPoints());
        surveyMenu.DropDownItems.Add(new ToolStripSeparator());
        surveyMenu.DropDownItems.Add("&Refresh Heatmap", null, (s, e) => _canvas.RefreshHeatmap());

        // Export Menu
        var exportMenu = new ToolStripMenuItem("&Export");
        exportMenu.DropDownItems.Add("Export to &CSV...", null, (s, e) => ExportToCsv());
        exportMenu.DropDownItems.Add("Export &Heatmap Image...", null, (s, e) => ExportHeatmapImage());
        exportMenu.DropDownItems.Add("Generate &Report...", null, (s, e) => GenerateReport());

        // Help Menu
        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add("&Instructions", null, (s, e) => ShowInstructions());
        helpMenu.DropDownItems.Add("&About", null, (s, e) => ShowAbout());

        _menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu, monitorMenu, surveyMenu, exportMenu, helpMenu });
    }

    private void CreateToolbar()
    {
        _toolStrip.Items.Add(new ToolStripButton("New", null, (s, e) => NewProject()) { ToolTipText = "New Project" });
        _toolStrip.Items.Add(new ToolStripButton("Open", null, (s, e) => OpenProject()) { ToolTipText = "Open Project" });
        _toolStrip.Items.Add(new ToolStripButton("Save", null, (s, e) => SaveProject()) { ToolTipText = "Save Project" });
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(new ToolStripButton("Live Monitor", null, (s, e) => _mainTabControl.SelectedTab = _liveMonitorTab) { ToolTipText = "Switch to Live Monitor" });
        _toolStrip.Items.Add(new ToolStripButton("Site Survey", null, (s, e) => _mainTabControl.SelectedTab = _siteSurveyTab) { ToolTipText = "Switch to Site Survey" });
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(new ToolStripButton("Load Image", null, (s, e) => LoadFloorPlan()) { ToolTipText = "Load Floor Plan Image" });
        _toolStrip.Items.Add(new ToolStripButton("Measure", null, (s, e) => TakeMeasurementAtCenter()) { ToolTipText = "Take WiFi Measurement" });
        _toolStrip.Items.Add(new ToolStripButton("Clear", null, (s, e) => ClearAllPoints()) { ToolTipText = "Clear All Measurements" });
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(new ToolStripButton("Export CSV", null, (s, e) => ExportToCsv()) { ToolTipText = "Export to CSV" });
        _toolStrip.Items.Add(new ToolStripButton("Export Image", null, (s, e) => ExportHeatmapImage()) { ToolTipText = "Export Heatmap Image" });
    }

    #endregion

    private void InitializeWifiScanner()
    {
        try
        {
            _wifiScanner = new WifiScanner();
            _wifiStatusLabel.Text = "WiFi: Ready";
            _wifiStatusLabel.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            _wifiStatusLabel.Text = $"WiFi: Error - {ex.Message}";
            _wifiStatusLabel.ForeColor = Color.Red;
            MessageBox.Show($"Failed to initialize WiFi scanner: {ex.Message}\n\nMeasurements will not be available.",
                "WiFi Scanner Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    #region Event Handlers

    private void OnCanvasClicked(object? sender, CanvasClickEventArgs e)
    {
        if (_canvas.FloorPlanImage == null)
        {
            MessageBox.Show("Please load a floor plan image first.", "No Floor Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        TakeMeasurement(e.NormalizedX, e.NormalizedY);
    }

    private void OnPointSelected(object? sender, MeasurementPoint? point)
    {
        _propertyGrid.SelectedObject = point;

        if (point != null)
        {
            // Select in list view
            foreach (ListViewItem item in _pointsListView.Items)
            {
                if (item.Tag is MeasurementPoint p && p.Id == point.Id)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }
    }

    private void OnPointHovered(object? sender, MeasurementPoint? point)
    {
        if (point != null)
        {
            _statusLabel.Text = $"{point.SSID} | {point.SignalStrength} dBm | Ch {point.Channel} | {point.QualityDescription}";
        }
        else
        {
            _statusLabel.Text = "Click on the floor plan to take a measurement";
        }
    }

    private void OnListViewSelectionChanged(object? sender, EventArgs e)
    {
        if (_pointsListView.SelectedItems.Count > 0 && _pointsListView.SelectedItems[0].Tag is MeasurementPoint point)
        {
            _canvas.SelectedPoint = point;
        }
    }

    #endregion

    #region Actions

    private void NewProject()
    {
        if (_project.IsDirty)
        {
            var result = MessageBox.Show("Save current project?", "Unsaved Changes",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return;
            if (result == DialogResult.Yes) SaveProject();
        }

        _project = new SurveyProject();
        _canvas.FloorPlanImage = null;
        _canvas.ClearMeasurementPoints();
        UpdateUI();
        _statusLabel.Text = "New project created";
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "WiFi Survey Project (*.wifisurvey)|*.wifisurvey|All Files (*.*)|*.*",
            Title = "Open Project"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var project = SurveyProject.Load(dialog.FileName);
            if (project != null)
            {
                _project = project;
                _canvas.FloorPlanImage = _project.FloorPlan.Image;
                _canvas.MeasurementPoints = _project.MeasurementPoints;
                UpdateUI();
                _statusLabel.Text = $"Opened: {Path.GetFileName(dialog.FileName)}";
            }
            else
            {
                MessageBox.Show("Failed to open project file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void SaveProject()
    {
        if (string.IsNullOrEmpty(_project.FilePath))
        {
            SaveProjectAs();
            return;
        }

        _project.Save(_project.FilePath);
        _statusLabel.Text = $"Saved: {Path.GetFileName(_project.FilePath)}";
    }

    private void SaveProjectAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "WiFi Survey Project (*.wifisurvey)|*.wifisurvey",
            Title = "Save Project As",
            FileName = _project.Name
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _project.Save(dialog.FileName);
            _statusLabel.Text = $"Saved: {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void LoadFloorPlan()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files (*.*)|*.*",
            Title = "Load Floor Plan Image"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var floorPlan = FloorPlan.FromFile(dialog.FileName);
            if (floorPlan != null)
            {
                _project.FloorPlan = floorPlan;
                _canvas.FloorPlanImage = floorPlan.Image;
                _canvas.FitToView();
                _project.IsDirty = true;
                _mainTabControl.SelectedTab = _siteSurveyTab;
                _statusLabel.Text = $"Loaded floor plan: {Path.GetFileName(dialog.FileName)}";
            }
        }
    }

    private async void TakeMeasurement(double x, double y)
    {
        if (_wifiScanner == null)
        {
            MessageBox.Show("WiFi scanner not available.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (_measurementInProgress) return;
        _measurementInProgress = true;

        _statusLabel.Text = "Scanning for survey measurement... keep the laptop in place";
        _progressBar.Visible = true;

        try
        {
            // Survey points must never silently use the old BSS cache.
            var scanResult = await _wifiScanner.ScanAsync();
            if (!scanResult.Succeeded)
                throw new InvalidOperationException($"Fresh scan required: {scanResult.Description}");
            if (IsDisposed || Disposing) return;

            var measurement = _wifiScanner.TakeMeasurement();
            var point = MeasurementPoint.FromMeasurement(x, y, measurement);

            _project.AddMeasurement(point);
            _canvas.AddMeasurementPoint(point);
            UpdatePointsList();
            UpdateStatistics();

            _statusLabel.Text = $"Measurement added: {point.SSID} @ {point.SignalStrength} dBm";
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                MessageBox.Show($"Failed to take measurement: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "Measurement failed";
            }
        }
        finally
        {
            _measurementInProgress = false;
            if (!IsDisposed && !Disposing) _progressBar.Visible = false;
        }
    }

    private void TakeMeasurementAtCenter()
    {
        if (_canvas.FloorPlanImage == null)
        {
            MessageBox.Show("Please load a floor plan first.\n\nFor quick WiFi testing without a floor plan, use the Live Monitor tab.",
                "No Floor Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        TakeMeasurement(0.5, 0.5);
    }

    private void ClearAllPoints()
    {
        if (_project.MeasurementPoints.Count == 0) return;

        var result = MessageBox.Show($"Delete all {_project.MeasurementPoints.Count} measurement points?",
            "Clear All", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _project.ClearMeasurements();
            _canvas.ClearMeasurementPoints();
            UpdateUI();
            _statusLabel.Text = "All measurements cleared";
        }
    }

    private void DeleteSelectedPoint()
    {
        if (_canvas.SelectedPoint != null)
        {
            _project.RemoveMeasurement(_canvas.SelectedPoint.Id);
            _canvas.RemoveMeasurementPoint(_canvas.SelectedPoint.Id);
            UpdateUI();
        }
    }

    private void ExportToCsv()
    {
        if (_project.MeasurementPoints.Count == 0)
        {
            MessageBox.Show("No measurements to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            Title = "Export to CSV",
            FileName = $"{_project.Name}_measurements"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,X,Y,SSID,BSSID,SignalStrength,LinkQuality,Channel,Band,Frequency,MaxRate,Note");

            foreach (var point in _project.MeasurementPoints)
            {
                sb.AppendLine($"{point.Timestamp:yyyy-MM-dd HH:mm:ss},{point.X:F4},{point.Y:F4}," +
                    $"\"{point.SSID}\",{point.BSSID},{point.SignalStrength},{point.LinkQuality}," +
                    $"{point.Channel},{point.Band},{point.Frequency:F1},{point.MaxRate:F1},\"{point.Note}\"");
            }

            File.WriteAllText(dialog.FileName, sb.ToString());
            _statusLabel.Text = $"Exported to: {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void ExportHeatmapImage()
    {
        if (_canvas.FloorPlanImage == null || _project.MeasurementPoints.Count < 3)
        {
            MessageBox.Show("Need a floor plan and at least 3 measurements to generate a heatmap.",
                "Export Heatmap", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
            Title = "Export Heatmap Image",
            FileName = $"{_project.Name}_heatmap"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var image = _project.FloorPlan.Image!;
            var heatmap = _heatmapGenerator.GenerateHeatmap(image.Width, image.Height, _project.MeasurementPoints);

            using var combined = new Bitmap(image.Width, image.Height);
            using (var g = Graphics.FromImage(combined))
            {
                g.DrawImage(image, 0, 0);
                g.DrawImage(heatmap, 0, 0);
            }

            combined.Save(dialog.FileName);
            heatmap.Dispose();
            _statusLabel.Text = $"Heatmap exported to: {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void GenerateReport()
    {
        if (_project.MeasurementPoints.Count == 0)
        {
            MessageBox.Show("No measurements to report.", "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "HTML Report (*.html)|*.html",
            Title = "Generate Report",
            FileName = $"{_project.Name}_report"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var stats = _project.GetStatistics();
            var html = GenerateHtmlReport(stats);
            File.WriteAllText(dialog.FileName, html);
            _statusLabel.Text = $"Report generated: {Path.GetFileName(dialog.FileName)}";

            // Open in browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dialog.FileName,
                UseShellExecute = true
            });
        }
    }

    private string GenerateHtmlReport(SurveyStatistics stats)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <title>WiFi Survey Report - {_project.Name}</title>
    <style>
        body {{ font-family: 'Segoe UI', sans-serif; margin: 40px; }}
        h1 {{ color: #333; }}
        .stats {{ display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }}
        .stat-card {{ background: #f5f5f5; padding: 15px; border-radius: 8px; }}
        .stat-value {{ font-size: 24px; font-weight: bold; color: #0066cc; }}
        table {{ border-collapse: collapse; width: 100%; margin-top: 20px; }}
        th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
        th {{ background: #0066cc; color: white; }}
        .excellent {{ color: #00aa00; }}
        .good {{ color: #88aa00; }}
        .fair {{ color: #ccaa00; }}
        .weak {{ color: #cc6600; }}
        .poor {{ color: #cc0000; }}
    </style>
</head>
<body>
    <h1>WiFi Survey Report</h1>
    <p><strong>Project:</strong> {_project.Name}</p>
    <p><strong>Location:</strong> {_project.Location}</p>
    <p><strong>Date:</strong> {DateTime.Now:yyyy-MM-dd HH:mm}</p>

    <h2>Summary Statistics</h2>
    <div class=""stats"">
        <div class=""stat-card"">
            <div class=""stat-value"">{stats.TotalPoints}</div>
            <div>Total Measurements</div>
        </div>
        <div class=""stat-card"">
            <div class=""stat-value"">{stats.AverageSignalStrength:F1} dBm</div>
            <div>Average Signal</div>
        </div>
        <div class=""stat-card"">
            <div class=""stat-value"">{stats.AverageLinkQuality:F0}%</div>
            <div>Average Link Quality</div>
        </div>
    </div>

    <h2>Coverage Distribution</h2>
    <table>
        <tr><th>Quality</th><th>Count</th><th>Percentage</th></tr>
        <tr class=""excellent""><td>Excellent (-30 to -50 dBm)</td><td>{stats.ExcellentCoverage}</td><td>{stats.CoveragePercentage("Excellent"):F1}%</td></tr>
        <tr class=""good""><td>Good (-50 to -60 dBm)</td><td>{stats.GoodCoverage}</td><td>{stats.CoveragePercentage("Good"):F1}%</td></tr>
        <tr class=""fair""><td>Fair (-60 to -70 dBm)</td><td>{stats.FairCoverage}</td><td>{stats.CoveragePercentage("Fair"):F1}%</td></tr>
        <tr class=""weak""><td>Weak (-70 to -80 dBm)</td><td>{stats.WeakCoverage}</td><td>{stats.CoveragePercentage("Weak"):F1}%</td></tr>
        <tr class=""poor""><td>Poor (below -80 dBm)</td><td>{stats.PoorCoverage}</td><td>{stats.CoveragePercentage("Poor"):F1}%</td></tr>
    </table>

    <h2>Measurement Points</h2>
    <table>
        <tr><th>Time</th><th>SSID</th><th>Signal</th><th>Channel</th><th>Band</th><th>Quality</th></tr>
        {string.Join("\n", _project.MeasurementPoints.Select(p => $@"
        <tr>
            <td>{p.Timestamp:HH:mm:ss}</td>
            <td>{p.SSID}</td>
            <td>{p.SignalStrength} dBm</td>
            <td>{p.Channel}</td>
            <td>{p.Band}</td>
            <td class=""{p.QualityDescription.ToLower()}"">{p.QualityDescription}</td>
        </tr>"))}
    </table>

    <p style=""margin-top: 40px; color: #666; font-size: 12px;"">
        Generated by WiFi Survey Tool - CosmicBytez IT Operations
    </p>
</body>
</html>";
    }

    private void ShowInstructions()
    {
        MessageBox.Show(
            "WiFi Survey Tool - Instructions\n\n" +
            "LIVE MONITOR TAB:\n" +
            "- View real-time signal strength graph\n" +
            "- See all visible WiFi networks\n" +
            "- Monitor connection quality over time\n" +
            "- Adjust auto-refresh rate (1-10 seconds)\n\n" +
            "SITE SURVEY TAB:\n" +
            "1. Load a floor plan image (File > Load Floor Plan)\n" +
            "2. Click on the floor plan to take WiFi measurements\n" +
            "3. The heatmap will automatically update\n" +
            "4. At least 3 measurements are needed for a heatmap\n\n" +
            "Controls:\n" +
            "- Left Click: Take measurement / Select point\n" +
            "- Right Click + Drag: Pan the view\n" +
            "- Mouse Wheel: Zoom in/out\n\n" +
            "Tips:\n" +
            "- Use Live Monitor to test WiFi before surveys\n" +
            "- Take measurements in a grid pattern for best results",
            "Instructions", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "WiFi Survey Tool v2.0\n\n" +
            "A professional WiFi site survey and monitoring tool.\n\n" +
            "Features:\n" +
            "- Live signal monitoring with real-time graph\n" +
            "- Floor plan-based site surveys\n" +
            "- Heat map visualization\n" +
            "- CSV export and HTML reports\n\n" +
            "CosmicBytez IT Operations\n" +
            "Part of the IT Tools Library",
            "About WiFi Survey Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    #endregion

    #region UI Updates

    private void UpdateUI()
    {
        UpdatePointsList();
        UpdateStatistics();
    }

    private void UpdatePointsList()
    {
        _pointsListView.Items.Clear();
        foreach (var point in _project.MeasurementPoints)
        {
            var item = new ListViewItem(point.SSID) { Tag = point };
            item.SubItems.Add($"{point.SignalStrength} dBm");
            item.SubItems.Add(point.Channel.ToString());
            item.SubItems.Add(point.QualityDescription);
            item.ForeColor = point.GetSignalColor();
            _pointsListView.Items.Add(item);
        }
    }

    private void UpdateStatistics()
    {
        var stats = _project.GetStatistics();
        _statsLabel.Text = stats.TotalPoints == 0 ? "No measurements" :
            $"Points: {stats.TotalPoints}\n" +
            $"Avg Signal: {stats.AverageSignalStrength:F1} dBm\n" +
            $"Min: {stats.MinSignalStrength} dBm\n" +
            $"Max: {stats.MaxSignalStrength} dBm\n" +
            $"Avg Quality: {stats.AverageLinkQuality:F0}%\n" +
            $"SSIDs: {stats.UniqueSSIDs}\n" +
            $"Channels: {stats.UniqueChannels}";
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _wifiScanner?.Dispose();
            _project.FloorPlan.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Comparer for sorting ListView items by column
/// </summary>
internal class ListViewItemComparer : System.Collections.IComparer
{
    private readonly int _column;
    private readonly SortOrder _order;

    public ListViewItemComparer(int column, SortOrder order)
    {
        _column = column;
        _order = order;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not ListViewItem itemX || y is not ListViewItem itemY)
            return 0;

        var textX = itemX.SubItems[_column].Text;
        var textY = itemY.SubItems[_column].Text;

        int result;

        // Column-specific parsing
        switch (_column)
        {
            case 2: // Signal (e.g., "-45 dBm")
                var signalX = int.TryParse(textX.Replace(" dBm", ""), out var sX) ? sX : -100;
                var signalY = int.TryParse(textY.Replace(" dBm", ""), out var sY) ? sY : -100;
                result = signalX.CompareTo(signalY);
                break;

            case 3: // Quality (e.g., "85%")
                var qualX = int.TryParse(textX.Replace("%", ""), out var qX) ? qX : 0;
                var qualY = int.TryParse(textY.Replace("%", ""), out var qY) ? qY : 0;
                result = qualX.CompareTo(qualY);
                break;

            case 4: // Channel (numeric)
                var chanX = int.TryParse(textX, out var cX) ? cX : 0;
                var chanY = int.TryParse(textY, out var cY) ? cY : 0;
                result = chanX.CompareTo(chanY);
                break;

            default: // SSID, BSSID and Band - string comparison
                result = string.Compare(textX, textY, StringComparison.OrdinalIgnoreCase);
                break;
        }

        return _order == SortOrder.Descending ? -result : result;
    }
}
