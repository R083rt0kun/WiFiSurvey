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

    #region Native WiFi API Structures and Constants

    private const int WLAN_MAX_NAME_LENGTH = 256;
    private const int DOT11_SSID_MAX_LENGTH = 32;
    private const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;
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
        {
            throw new InvalidOperationException($"Failed to open WLAN handle. Error code: {result}");
        }
    }

    /// <summary>
    /// Gets all available WiFi networks with signal information
    /// </summary>
    public List<WifiNetwork> GetAvailableNetworks()
    {
        var networks = new List<WifiNetwork>();

        if (_clientHandle == IntPtr.Zero)
            return networks;

        IntPtr interfaceListPtr;
        int result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out interfaceListPtr);

        if (result != 0)
            return networks;

        try
        {
            var interfaceList = new WLAN_INTERFACE_INFO_LIST(interfaceListPtr);

            foreach (var iface in interfaceList.InterfaceInfo)
            {
                Guid interfaceGuid = iface.InterfaceGuid;

                // Trigger a scan
                WlanScan(_clientHandle, ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(500); // Wait for scan to complete

                // Get BSS list for detailed info (RSSI, channel)
                IntPtr bssListPtr;
                result = WlanGetNetworkBssList(
                    _clientHandle,
                    ref interfaceGuid,
                    IntPtr.Zero,
                    3, // dot11_BSS_type_any
                    false,
                    IntPtr.Zero,
                    out bssListPtr);

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

                            string ssid = Encoding.ASCII.GetString(bssEntry.dot11Ssid.ucSSID, 0, bssEntry.dot11Ssid.uSSIDLength);
                            string bssid = FormatBssid(bssEntry.dot11Bssid);
                            int channel = FrequencyToChannel(bssEntry.ulChCenterFrequency);
                            int rssi = bssEntry.lRssi;
                            int linkQuality = (int)bssEntry.uLinkQuality;

                            // Calculate max rate from rate set
                            double maxRate = 0;
                            if (bssEntry.wlanRateSet.usRateSet != null)
                            {
                                for (int r = 0; r < bssEntry.wlanRateSet.uRateSetLength && r < 126; r++)
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
    /// Resolves the connected AP from the scan results by its real BSSID.
    /// </summary>
    public WifiNetwork? GetConnectedNetwork()
    {
        string? connectedBssid = GetConnectedBssid();
        if (string.IsNullOrEmpty(connectedBssid))
            return null;

        return GetAvailableNetworks().FirstOrDefault(n =>
            string.Equals(n.BSSID, connectedBssid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs a measurement at the current location
    /// </summary>
    public WifiMeasurement TakeMeasurement()
    {
        var networks = GetAvailableNetworks();
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

        // 6 GHz band (WiFi 6E)
        if (frequencyMhz >= 5935 && frequencyMhz <= 7115)
        {
            return (int)((frequencyMhz - 5950) / 5) + 1;
        }

        return 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_clientHandle != IntPtr.Zero)
            {
                WlanCloseHandle(_clientHandle, IntPtr.Zero);
                _clientHandle = IntPtr.Zero;
            }
            _disposed = true;
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
    public string SignalQualityDescription => SignalStrength switch
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
