using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace WifiSurvey.Services;

/// <summary>
/// WiFi scanning service using the Native WiFi API (wlanapi.dll)
/// Provides signal strength, SSID, channel, and other WiFi metrics
/// </summary>
public sealed class WifiScanner : IDisposable
{
    private IntPtr _clientHandle = IntPtr.Zero;
    private bool _disposed;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<int>> _pendingScans = new();
    // Keep the managed delegate alive for as long as the native WLAN handle is open.
    private WlanNotificationCallback? _notificationCallback;
    private readonly CancellationTokenSource _disposeCancellation = new();

    public enum ScanState { Completed, Failed, TimedOut, NoInterfaces }
    public sealed record ScanResult(ScanState State, string Description, DateTime Timestamp)
    {
        public bool Succeeded => State == ScanState.Completed;
    }

    public DateTime? LastCompletedScan { get; private set; }
    private List<WifiNetwork> _lastNonEmptyNetworks = new();
    public bool LastReadUsedOldResults { get; private set; }

    #region Native WiFi API Structures and Constants

    private const int WLAN_MAX_NAME_LENGTH = 256;
    private const int DOT11_SSID_MAX_LENGTH = 32;
    private const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;
    private const int WLAN_INTF_OPCODE_RSSI = 0x10000102;
    private const uint WLAN_NOTIFICATION_SOURCE_ACM = 0x00000008;
    private const uint WLAN_NOTIFICATION_ACM_SCAN_COMPLETE = 7;
    private const uint WLAN_NOTIFICATION_ACM_SCAN_FAIL = 8;
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(5);
    private const int WLAN_AVAILABLE_NETWORK_INCLUDE_ALL_ADHOC_PROFILES = 0x00000001;
    private const int WLAN_AVAILABLE_NETWORK_INCLUDE_ALL_MANUAL_HIDDEN_PROFILES = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WLAN_MAX_NAME_LENGTH)]
        public string strInterfaceDescription;
        public int isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_INTERFACE_INFO_LIST
    {
        public int dwNumberOfItems;
        public int dwIndex;
        public WLAN_INTERFACE_INFO[] InterfaceInfo;

        public WLAN_INTERFACE_INFO_LIST(IntPtr pList)
        {
            dwNumberOfItems = Marshal.ReadInt32(pList);
            dwIndex = Marshal.ReadInt32(pList, 4);
            InterfaceInfo = new WLAN_INTERFACE_INFO[dwNumberOfItems];

            for (int i = 0; i < dwNumberOfItems; i++)
            {
                IntPtr pItemList = new IntPtr(pList.ToInt64() + 8 + (Marshal.SizeOf(typeof(WLAN_INTERFACE_INFO)) * i));
                InterfaceInfo[i] = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(pItemList);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_SSID
    {
        public int uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DOT11_SSID_MAX_LENGTH)]
        public byte[] ucSSID;
    }

    // WLAN_NOTIFICATION_DATA and native callback are used for asynchronous scans.
    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_NOTIFICATION_DATA
    {
        public uint NotificationSource;
        public uint NotificationCode;
        public Guid InterfaceGuid;
        public uint DataSize;
        public IntPtr DataPointer;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WlanNotificationCallback(ref WLAN_NOTIFICATION_DATA notification, IntPtr context);

    // This matches the prefix of native WLAN_CONNECTION_ATTRIBUTES
    // through wlanAssociationAttributes.dot11Bssid.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_PREFIX
    {
        public int InterfaceState;
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WLAN_MAX_NAME_LENGTH)]
        public string ProfileName;

        public DOT11_SSID Ssid;
        public int BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;
    }

    // Native WLAN_CONNECTION_ATTRIBUTES prefix, ending at
    // wlanAssociationAttributes.ulTxRate. Rates are reported in Kbits/s.
    // This is distinct from the legacy supported-rate set in WLAN_BSS_ENTRY.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_LINK_RATES_PREFIX
    {
        public int InterfaceState;
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WLAN_MAX_NAME_LENGTH)]
        public string ProfileName;

        public DOT11_SSID Ssid;
        public int BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;

        public int PhyType;
        public uint PhyIndex;
        public uint DriverSignalQuality;
        public uint RxRateKbps;
        public uint TxRateKbps;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_AVAILABLE_NETWORK
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WLAN_MAX_NAME_LENGTH)]
        public string strProfileName;
        public DOT11_SSID dot11Ssid;
        public int dot11BssType;
        public uint uNumberOfBssids;
        public bool bNetworkConnectable;
        public uint wlanNotConnectableReason;
        public uint uNumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] dot11PhyTypes;
        public bool bMorePhyTypes;
        public uint wlanSignalQuality;
        public bool bSecurityEnabled;
        public int dot11DefaultAuthAlgorithm;
        public int dot11DefaultCipherAlgorithm;
        public uint dwFlags;
        public uint dwReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_BSS_ENTRY
    {
        public DOT11_SSID dot11Ssid;
        public uint uPhyId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;
        public int dot11BssType;
        public int dot11BssPhyType;
        public int lRssi;
        public uint uLinkQuality;
        public bool bInRegDomain;
        public ushort usBeaconPeriod;
        public ulong ullTimestamp;
        public ulong ullHostTimestamp;
        public ushort usCapabilityInformation;
        public uint ulChCenterFrequency;
        public WLAN_RATE_SET wlanRateSet;
        public uint ulIeOffset;
        public uint ulIeSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_RATE_SET
    {
        public uint uRateSetLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
        public ushort[] usRateSet;
    }

    #endregion

    #region Native WiFi API Imports

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanOpenHandle(
        uint dwClientVersion,
        IntPtr pReserved,
        out uint pdwNegotiatedVersion,
        out IntPtr phClientHandle);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanCloseHandle(
        IntPtr hClientHandle,
        IntPtr pReserved);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanEnumInterfaces(
        IntPtr hClientHandle,
        IntPtr pReserved,
        out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanGetAvailableNetworkList(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        uint dwFlags,
        IntPtr pReserved,
        out IntPtr ppAvailableNetworkList);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanGetNetworkBssList(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        IntPtr pDot11Ssid,
        int dot11BssType,
        bool bSecurityEnabled,
        IntPtr pReserved,
        out IntPtr ppWlanBssList);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanScan(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        IntPtr pDot11Ssid,
        IntPtr pIeData,
        IntPtr pReserved);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanQueryInterface(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        int opCode,
        IntPtr pReserved,
        out uint pdwDataSize,
        out IntPtr ppData,
        IntPtr pWlanOpcodeValueType);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanRegisterNotification(
        IntPtr hClientHandle,
        uint dwNotifSource,
        [MarshalAs(UnmanagedType.Bool)] bool bIgnoreDuplicate,
        WlanNotificationCallback? callback,
        IntPtr pCallbackContext,
        IntPtr pReserved,
        IntPtr pdwPrevNotifSource);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    #endregion

    /// <summary>
    /// Initializes the WiFi scanner and opens a handle to the WLAN API
    /// </summary>
    public WifiScanner()
    {
        uint negotiatedVersion;
        int result = WlanOpenHandle(2, IntPtr.Zero, out negotiatedVersion, out _clientHandle);

        if (result != 0)
            throw new InvalidOperationException($"Failed to open WLAN handle. Error code: {result}");

        _notificationCallback = OnWlanNotification;
        result = WlanRegisterNotification(_clientHandle, WLAN_NOTIFICATION_SOURCE_ACM,
            false, _notificationCallback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (result != 0)
        {
            WlanCloseHandle(_clientHandle, IntPtr.Zero);
            _clientHandle = IntPtr.Zero;
            throw new InvalidOperationException($"Failed to register WLAN scan notifications. Error code: {result}");
        }
    }

    private void OnWlanNotification(ref WLAN_NOTIFICATION_DATA notification, IntPtr context)
    {
        if (notification.NotificationSource != WLAN_NOTIFICATION_SOURCE_ACM ||
            (notification.NotificationCode != WLAN_NOTIFICATION_ACM_SCAN_COMPLETE &&
             notification.NotificationCode != WLAN_NOTIFICATION_ACM_SCAN_FAIL))
            return;

        if (_pendingScans.TryGetValue(notification.InterfaceGuid, out var waiter))
        {
            int reason = 0;
            if (notification.NotificationCode == WLAN_NOTIFICATION_ACM_SCAN_FAIL)
            {
                reason = notification.DataPointer != IntPtr.Zero && notification.DataSize >= 4
                    ? Marshal.ReadInt32(notification.DataPointer) : -1;
                if (reason == 0) reason = -1; // A failure is never success.
            }
            waiter.TrySetResult(reason);
        }
    }

    /// <summary>
    /// Starts a real WLAN scan and awaits the corresponding ACM notification.
    /// Does not block the WinForms UI thread. No fixed Thread.Sleep guess.
    /// </summary>
    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCancellation.Token);
        var token = linkedCancellation.Token;
        await _scanGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr interfaceListPtr);
            if (result != 0 || interfaceListPtr == IntPtr.Zero)
                return new ScanResult(ScanState.Failed, $"WlanEnumInterfaces error {result}", DateTime.Now);

            try
            {
                var interfaces = new WLAN_INTERFACE_INFO_LIST(interfaceListPtr);
                if (interfaces.InterfaceInfo.Length == 0)
                    return new ScanResult(ScanState.NoInterfaces, "No Wi-Fi adapters found", DateTime.Now);

                int successes = 0;
                int timeouts = 0;
                var failures = new List<string>();

                foreach (var iface in interfaces.InterfaceInfo)
                {
                    token.ThrowIfCancellationRequested();
                    Guid interfaceGuid = iface.InterfaceGuid;
                    var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!_pendingScans.TryAdd(interfaceGuid, waiter))
                    {
                        failures.Add("A scan is already pending for one interface");
                        continue;
                    }

                    try
                    {
                        result = WlanScan(_clientHandle, ref interfaceGuid,
                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                        if (result != 0)
                        {
                            failures.Add($"WlanScan error {result}");
                            continue;
                        }

                        var timeout = Task.Delay(ScanTimeout, token);
                        var completedTask = await Task.WhenAny(waiter.Task, timeout).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (completedTask == waiter.Task)
                        {
                            int code = await waiter.Task.ConfigureAwait(false);
                            if (code == 0) successes++;
                            else failures.Add($"Scan failed (reason {code})");
                        }
                        else
                        {
                            timeouts++;
                        }
                    }
                    finally
                    {
                        _pendingScans.TryRemove(interfaceGuid, out _);
                    }
                }

                var now = DateTime.Now;
                if (successes > 0)
                {
                    LastCompletedScan = now;
                    string detail = failures.Count + timeouts > 0
                        ? $" ({failures.Count} failed, {timeouts} timed out on other adapters)" : "";
                    return new ScanResult(ScanState.Completed, "Scan complete" + detail, now);
                }

                if (timeouts > 0)
                    return new ScanResult(ScanState.TimedOut,
                        $"Scan timed out after {ScanTimeout.TotalSeconds:0}s; cached results may be stale", now);
                return new ScanResult(ScanState.Failed,
                    failures.Count > 0 ? string.Join("; ", failures) : "Scan did not complete", now);
            }
            finally
            {
                WlanFreeMemory(interfaceListPtr);
            }
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <summary>
    /// Reads Windows' last available BSS list without triggering another scan.
    /// </summary>
    public List<WifiNetwork> GetAvailableNetworks(bool allowStaleFallback = true)
    {
        var networks = new List<WifiNetwork>();

        if (_clientHandle == IntPtr.Zero || _disposed)
            throw new ObjectDisposedException(nameof(WifiScanner));

        int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr interfaceListPtr);
        if (result != 0 || interfaceListPtr == IntPtr.Zero)
            throw new InvalidOperationException($"WlanEnumInterfaces error {result}");

        try
        {
            var interfaceList = new WLAN_INTERFACE_INFO_LIST(interfaceListPtr);

            foreach (var iface in interfaceList.InterfaceInfo)
            {
                Guid interfaceGuid = iface.InterfaceGuid;

                // No scan here: the independent ScanAsync method triggers and
                // awaits scans. Live Monitor may read cached BSS every 2 seconds.
                // Get BSS list for detailed info (RSSI, channel).
                IntPtr bssListPtr;
                result = WlanGetNetworkBssList(
                    _clientHandle,
                    ref interfaceGuid,
                    IntPtr.Zero,
                    3, // dot11_BSS_type_any
                    false,
                    IntPtr.Zero,
                    out bssListPtr);

                if (result != 0 || bssListPtr == IntPtr.Zero)
                    throw new InvalidOperationException($"WlanGetNetworkBssList error {result}");

                if (result == 0 && bssListPtr != IntPtr.Zero)
                {
                    try
                    {
                        int totalSize = Marshal.ReadInt32(bssListPtr);
                        int numberOfItems = Marshal.ReadInt32(bssListPtr, 4);

                        for (int i = 0; i < numberOfItems; i++)
                        {
                            IntPtr bssEntryPtr = new IntPtr(bssListPtr.ToInt64() + 8 + (i * Marshal.SizeOf(typeof(WLAN_BSS_ENTRY))));
                            var bssEntry = Marshal.PtrToStructure<WLAN_BSS_ENTRY>(bssEntryPtr);

                            int ssidLength = Math.Clamp(bssEntry.dot11Ssid.uSSIDLength, 0, DOT11_SSID_MAX_LENGTH);
                            string ssid = Encoding.UTF8.GetString(bssEntry.dot11Ssid.ucSSID, 0, ssidLength);
                            string bssid = FormatBssid(bssEntry.dot11Bssid);
                            int channel = FrequencyToChannel(bssEntry.ulChCenterFrequency);
                            int rssi = bssEntry.lRssi;
                            int linkQuality = (int)bssEntry.uLinkQuality;

                            // Legacy supported rates of this BSS, NOT the current
                            // connection PHY rate or the maximum 802.11ac/ax rate.
                            double maxRate = 0;
                            if (bssEntry.wlanRateSet.usRateSet != null)
                            {
                                // uRateSetLength counts bytes, with each entry a ushort.
                                int count = (int)Math.Min(bssEntry.wlanRateSet.uRateSetLength / sizeof(ushort),
                                    (uint)bssEntry.wlanRateSet.usRateSet.Length);
                                for (int r = 0; r < count; r++)
                                {
                                    double rate = (bssEntry.wlanRateSet.usRateSet[r] & 0x7FFF) * 0.5;
                                    if (rate > maxRate) maxRate = rate;
                                }
                            }

                            networks.Add(new WifiNetwork
                            {
                                SSID = ssid,
                                BSSID = bssid,
                                SignalStrength = rssi,
                                LinkQuality = linkQuality,
                                Channel = channel,
                                Frequency = bssEntry.ulChCenterFrequency / 1000.0, // Convert to MHz
                                MaxRate = maxRate,
                                InterfaceName = iface.strInterfaceDescription,
                                Timestamp = DateTime.Now
                            });
                        }
                    }
                    finally
                    {
                        WlanFreeMemory(bssListPtr);
                    }
                }
            }
        }
        finally
        {
            WlanFreeMemory(interfaceListPtr);
        }

        // An empty OS scan cache is not evidence that all access points vanished.
        // For display only, retain the last nonempty observation and label it stale.
        // Site Survey calls this method with allowStaleFallback=false.
        if (networks.Count == 0 && allowStaleFallback && _lastNonEmptyNetworks.Count > 0)
        {
            LastReadUsedOldResults = true;
            return new List<WifiNetwork>(_lastNonEmptyNetworks);
        }
        LastReadUsedOldResults = false;
        if (networks.Count > 0)
            _lastNonEmptyNetworks = new List<WifiNetwork>(networks);
        return networks;
    }

    /// <summary>
    /// Gets the actual connected AP BSSID from the Windows Native Wi-Fi API.
    /// Returns null if no interface is connected or the query is unavailable.
    /// Never guesses by strongest signal.
    /// </summary>
    public string? GetConnectedBssid()
    {
        if (_clientHandle == IntPtr.Zero || _disposed)
            return null;

        int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr interfaceListPtr);
        if (result != 0 || interfaceListPtr == IntPtr.Zero)
            return null;

        try
        {
            var interfaces = new WLAN_INTERFACE_INFO_LIST(interfaceListPtr);

            foreach (var iface in interfaces.InterfaceInfo)
            {
                // wlan_interface_state_connected == 1
                if (iface.isState != 1)
                    continue;

                Guid interfaceGuid = iface.InterfaceGuid;
                result = WlanQueryInterface(
                    _clientHandle,
                    ref interfaceGuid,
                    WLAN_INTF_OPCODE_CURRENT_CONNECTION,
                    IntPtr.Zero,
                    out uint dataSize,
                    out IntPtr dataPtr,
                    IntPtr.Zero);

                try
                {
                    if (result != 0 || dataPtr == IntPtr.Zero ||
                        dataSize < (uint)Marshal.SizeOf<WLAN_CONNECTION_PREFIX>())
                        continue;

                    var connection = Marshal.PtrToStructure<WLAN_CONNECTION_PREFIX>(dataPtr);
                    return FormatBssid(connection.Bssid);
                }
                finally
                {
                    if (dataPtr != IntPtr.Zero)
                        WlanFreeMemory(dataPtr);
                }
            }

            return null;
        }
        finally
        {
            WlanFreeMemory(interfaceListPtr);
        }
    }

    /// <summary>
    /// Returns the SSID advertised by the actual connected BSS from Windows
    /// association attributes, even when that BSSID is absent in the BSS cache.
    /// The caller supplies the observed BSSID to avoid mixing two associations
    /// when the client roams between queries.
    /// </summary>
    public string? GetConnectedSsid(string expectedBssid)
    {
        if (_clientHandle == IntPtr.Zero || _disposed || string.IsNullOrWhiteSpace(expectedBssid))
            return null;

        int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr listPtr);
        if (result != 0 || listPtr == IntPtr.Zero)
            return null;

        try
        {
            var interfaces = new WLAN_INTERFACE_INFO_LIST(listPtr);
            foreach (var iface in interfaces.InterfaceInfo)
            {
                if (iface.isState != 1) continue;
                Guid guid = iface.InterfaceGuid;
                result = WlanQueryInterface(_clientHandle, ref guid,
                    WLAN_INTF_OPCODE_CURRENT_CONNECTION, IntPtr.Zero,
                    out uint size, out IntPtr data, IntPtr.Zero);
                try
                {
                    if (result != 0 || data == IntPtr.Zero ||
                        size < (uint)Marshal.SizeOf<WLAN_CONNECTION_PREFIX>())
                        continue;
                    var info = Marshal.PtrToStructure<WLAN_CONNECTION_PREFIX>(data);
                    if (!string.Equals(FormatBssid(info.Bssid), expectedBssid,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    int length = Math.Clamp(info.Ssid.uSSIDLength, 0, DOT11_SSID_MAX_LENGTH);
                    return info.Ssid.ucSSID == null ? null :
                        Encoding.UTF8.GetString(info.Ssid.ucSSID, 0, length);
                }
                finally
                {
                    if (data != IntPtr.Zero)
                        WlanFreeMemory(data);
                }
            }
            return null;
        }
        finally
        {
            WlanFreeMemory(listPtr);
        }
    }

    /// <summary>
    /// Returns the receive and transmit PHY link rates reported by Windows for
    /// the current association. These are neither application throughput nor
    /// the maximum supported legacy rates of a BSS. Returns null when unavailable
    /// or when roaming has changed the BSSID since the caller read its BSS list.
    /// </summary>
    public (double RxMbps, double TxMbps)? GetConnectedLinkRates(string expectedBssid)
    {
        if (_clientHandle == IntPtr.Zero || _disposed || string.IsNullOrWhiteSpace(expectedBssid))
            return null;

        int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr interfaceListPtr);
        if (result != 0 || interfaceListPtr == IntPtr.Zero)
            return null;

        try
        {
            var interfaces = new WLAN_INTERFACE_INFO_LIST(interfaceListPtr);
            foreach (var iface in interfaces.InterfaceInfo)
            {
                if (iface.isState != 1) // wlan_interface_state_connected
                    continue;

                Guid interfaceGuid = iface.InterfaceGuid;
                result = WlanQueryInterface(
                    _clientHandle,
                    ref interfaceGuid,
                    WLAN_INTF_OPCODE_CURRENT_CONNECTION,
                    IntPtr.Zero,
                    out uint dataSize,
                    out IntPtr dataPtr,
                    IntPtr.Zero);

                try
                {
                    if (result != 0 || dataPtr == IntPtr.Zero ||
                        dataSize < (uint)Marshal.SizeOf<WLAN_CONNECTION_LINK_RATES_PREFIX>())
                        continue;

                    var info = Marshal.PtrToStructure<WLAN_CONNECTION_LINK_RATES_PREFIX>(dataPtr);
                    if (!string.Equals(FormatBssid(info.Bssid), expectedBssid,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (info.RxRateKbps == 0 && info.TxRateKbps == 0)
                        return null;

                    return (info.RxRateKbps / 1000.0, info.TxRateKbps / 1000.0);
                }
                finally
                {
                    if (dataPtr != IntPtr.Zero)
                        WlanFreeMemory(dataPtr);
                }
            }

            return null;
        }
        finally
        {
            WlanFreeMemory(interfaceListPtr);
        }
    }

    /// <summary>
    /// Queries the connected interface RSSI directly. Unlike cached BSS entries,
    /// this does not require an RF scan and may be called on every monitor tick.
    /// Returns null if the driver does not support this opcode.
    /// </summary>
    public int? GetConnectedRssi()
    {
        if (_clientHandle == IntPtr.Zero || _disposed) return null;
        int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr listPointer);
        if (result != 0 || listPointer == IntPtr.Zero) return null;

        try
        {
            var interfaces = new WLAN_INTERFACE_INFO_LIST(listPointer);
            foreach (var iface in interfaces.InterfaceInfo)
            {
                if (iface.isState != 1) continue; // connected
                Guid interfaceGuid = iface.InterfaceGuid;
                result = WlanQueryInterface(_clientHandle, ref interfaceGuid, WLAN_INTF_OPCODE_RSSI,
                    IntPtr.Zero, out uint dataSize, out IntPtr dataPointer, IntPtr.Zero);
                try
                {
                    if (result == 0 && dataPointer != IntPtr.Zero && dataSize >= 4)
                    {
                        int rssi = Marshal.ReadInt32(dataPointer);
                        if (rssi >= -127 && rssi <= 0) return rssi;
                    }
                }
                finally
                {
                    if (dataPointer != IntPtr.Zero) WlanFreeMemory(dataPointer);
                }
            }
            return null;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    /// <summary>
    /// Resolves the connected AP from the scan results by its real BSSID.
    /// </summary>
    public WifiNetwork? GetConnectedNetwork()
    {
        string? connectedBssid = GetConnectedBssid();
        if (string.IsNullOrEmpty(connectedBssid))
            return null;

        return GetAvailableNetworks(allowStaleFallback: false).FirstOrDefault(n =>
            string.Equals(n.BSSID, connectedBssid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs a measurement at the current location
    /// </summary>
    public WifiMeasurement TakeMeasurement()
    {
        var networks = GetAvailableNetworks(allowStaleFallback: false);
        string? connectedBssid = GetConnectedBssid();
        if (string.IsNullOrEmpty(connectedBssid))
            throw new InvalidOperationException(
                "Cannot determine the currently connected Wi-Fi BSSID. No measurement was saved.");

        var connected = networks.FirstOrDefault(n =>
            string.Equals(n.BSSID, connectedBssid, StringComparison.OrdinalIgnoreCase));
        if (connected is null)
            throw new InvalidOperationException(
                $"Connected AP {connectedBssid} not present in scan. No measurement was saved. Retry scan.");

        return new WifiMeasurement
        {
            Timestamp = DateTime.Now,
            ConnectedNetwork = connected,
            VisibleNetworks = networks,
            SignalStrength = connected?.SignalStrength ?? -100,
            LinkQuality = connected?.LinkQuality ?? 0,
            Channel = connected?.Channel ?? 0
        };
    }

    private static string FormatBssid(byte[] bssid)
    {
        if (bssid == null || bssid.Length < 6)
            return string.Empty;

        return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
            bssid[0], bssid[1], bssid[2], bssid[3], bssid[4], bssid[5]);
    }

    private static int FrequencyToChannel(uint frequencyKhz)
    {
        uint frequencyMhz = frequencyKhz / 1000;

        // 2.4 GHz band
        if (frequencyMhz >= 2412 && frequencyMhz <= 2484)
        {
            if (frequencyMhz == 2484) return 14;
            return (int)((frequencyMhz - 2412) / 5) + 1;
        }

        // 5 GHz band
        if (frequencyMhz >= 5170 && frequencyMhz <= 5825)
        {
            return (int)((frequencyMhz - 5000) / 5);
        }

        // 6 GHz: the special 20 MHz channel 2 is centered at 5935 MHz.
        // Standard channel numbering uses channel 1 at 5955 MHz, with
        // 5 MHz channel-number spacing: channel = (MHz - 5950) / 5.
        if (frequencyMhz == 5935) return 2;
        if (frequencyMhz >= 5955 && frequencyMhz <= 7115 &&
            (frequencyMhz - 5950) % 5 == 0)
        {
            return (int)((frequencyMhz - 5950) / 5);
        }

        return 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _disposeCancellation.Cancel();
            // Closing the WLAN handle unregisters notifications. Never unregister
            // inside the callback (the native API could deadlock).
            if (_clientHandle != IntPtr.Zero)
            {
                WlanCloseHandle(_clientHandle, IntPtr.Zero);
                _clientHandle = IntPtr.Zero;
            }
        }
    }
}

/// <summary>
/// Represents a WiFi network with its properties
/// </summary>
public class WifiNetwork
{
    public string SSID { get; set; } = string.Empty;
    public string BSSID { get; set; } = string.Empty;
    public int SignalStrength { get; set; } // RSSI in dBm
    public int LinkQuality { get; set; } // 0-100 percentage
    public int Channel { get; set; }
    public double Frequency { get; set; } // MHz
    public double MaxRate { get; set; } // Mbps
    public string InterfaceName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets a description of signal quality
    /// </summary>
    public string SignalQualityDescription => DescribeSignalStrength(SignalStrength);

    public static string DescribeSignalStrength(int rssi) => rssi switch
    {
        >= -50 => "Excellent",
        >= -60 => "Good",
        >= -70 => "Fair",
        >= -80 => "Weak",
        _ => "Poor"
    };

    /// <summary>
    /// Gets the WiFi band (2.4GHz, 5GHz, or 6GHz)
    /// </summary>
    public string Band => Frequency switch
    {
        >= 2400 and < 2500 => "2.4 GHz",
        >= 5150 and < 5900 => "5 GHz",
        >= 5925 and < 7125 => "6 GHz",
        _ => "Unknown"
    };
}

/// <summary>
/// Represents a WiFi measurement at a specific point in time
/// </summary>
public class WifiMeasurement
{
    public DateTime Timestamp { get; set; }
    public WifiNetwork? ConnectedNetwork { get; set; }
    public List<WifiNetwork> VisibleNetworks { get; set; } = new();
    public int SignalStrength { get; set; }
    public int LinkQuality { get; set; }
    public int Channel { get; set; }
}
