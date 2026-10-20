# WiFiSurvey Knowledge Base

This document explains what WiFiSurvey measures, how to interpret its metrics, and which conclusions the available data **does not** support. It is intended for users and maintainers. The application's Help integration will be addressed separately during the UI/UX phase.

## PHY Rx/Tx (Mbps)

**Definition.** The physical-layer (PHY) receive and transmit link rates reported by Windows for the **currently associated Wi-Fi interface**.

- **PHY Rx**: the reported receive link rate, from the access point toward the computer.
- **PHY Tx**: the reported transmit link rate, from the computer toward the access point.
- **Mbps** means megabits per second (not megabytes per second).

**Example:** `PHY Rx/Tx: 866.7/866.7 Mbps` means that Windows reports 866.7 Mbps in each direction as the current PHY link rates. It does **not** mean that the user can simultaneously transfer 866.7 Mbps in both directions, and it is **not** a measured throughput value.

**Interpretation.** Link rates reflect the PHY transmission settings negotiated/adapted by the devices. They can change as radio conditions and rate adaptation change. The useful application or internet throughput will typically be lower because of contention, medium access, protocol overhead, retransmissions, and other constraints. Wi-Fi channel airtime is shared; these two figures must not be added together to estimate throughput.

**Limitations.** PHY Rx/Tx cannot by themselves prove internet speed, roaming quality, packet loss, latency, or indoor coverage. They are not the maximum data rates advertised by neighboring BSSs. Only the currently associated link has these measured Windows connection attributes. When the connection changes during a read, the application should avoid reporting rates for the wrong BSSID.

**Where these values come from.** Windows Native Wi-Fi `WlanQueryInterface` (`wlan_intf_opcode_current_connection`), specifically association attributes in `WLAN_CONNECTION_ATTRIBUTES`. The underlying Windows PHY rates are in kilobits per second and are displayed in megabits per second.

**How to verify.** Run `netsh wlan show interfaces` and compare `Receive rate (Mbps)` and `Transmit rate (Mbps)` against `PHY Rx/Tx`. Small differences over time are possible.

## RSSI (dBm)

**Definition.** Received signal strength indication as reported by the Wi-Fi adapter. In typical Wi-Fi observations the number is negative; a value closer to zero indicates a stronger received signal (for example, -50 dBm is stronger than -70 dBm). RSSI is not throughput and does not independently measure interference or packet loss.

**Survey use.** RSSI is the primary observable used by this application's signal-strength history and coverage assessments. The labels *Excellent*, *Good*, *Fair*, *Weak*, and *Poor* are application-defined **RSSI grades**, not driver link-quality percentages, guarantees of performance, or standards-based pass/fail thresholds. They should be documented with their exact cutoffs.

## Driver link-quality value (0–100)

**Definition.** An adapter/driver-reported quality indicator (`WLAN_BSS_ENTRY.uLinkQuality`), with values ranging from 0 to 100. It is *not* a universal percentage of signal power, advertised or achieved data rate, or end-to-end service quality. Mapping and behavior may vary by driver.

**Policy.** Preserve the raw value in the internal model for troubleshooting, but do not use it as the principal coverage metric, as an RSSI substitute, or as an objective performance score. It is hidden from the default Live Monitor table, primary Site Survey statistics, standard CSV export, and summary HTML report.

## BSSID versus SSID

- **SSID:** the network name. Different BSSs can advertise the same SSID.
- **BSSID:** the identifier of a particular basic service set, usually displayed as a MAC address. One physical access point can advertise multiple BSSIDs.

In Live Monitor, each row identifies a BSS (with both SSID and BSSID); a count of distinct BSSIDs is not necessarily a count of physical access points.

## Current CSV `MaxRate` caveat

The existing `MaxRate` field in Site Survey CSV export comes from BSS rate information rather than the current association PHY Rx/Tx rate. It must **not** be interpreted as measured Wi-Fi throughput or as the negotiated current link rate. This legacy export field needs separate review before it is presented as a performance metric.

## Terms not to confuse

- **PHY link rate:** an adapter-reported radio-link data rate, in Mbps.
- **Throughput:** payload/application data actually delivered over a measured interval; requires a separate active performance test.
- **RSSI:** received signal-strength measurement, in dBm.
- **Driver quality:** opaque indicator in the range 0–100, not a normalized percentage of Wi-Fi performance.
- **RSSI grade:** WiFiSurvey's category derived from RSSI thresholds.

## Maintenance

When adding a metric to the application, document its source/API, units, scope (connected interface versus scanned BSS versus survey point), update frequency, interpretation, limitations, and verification procedure. Do not convert an undocumented or vendor-specific estimate into a standards-based claim.

## Live Monitor: signal history and scan status

- **Signal history** plots individual RSSI observations against their actual
  timestamps within a sliding 60-second window. The chart does not join values
  obtained from different BSSIDs. A change of connected AP clears the old
  trace and briefly shows an `AP changed` annotation.
- **Last successful scan** means that Windows acknowledged at least one WLAN
  scan completion. It does **not** guarantee that every BSS entry is freshly
  measured or that every available AP is visible. Wi-Fi discovery is subject to
  hardware, driver, channel and RF limitations.
- **SCAN UNCONFIRMED (BSS cache)** means that the most recent requested scan
  was not confirmed (or no successful scan has been observed in this session).
  A nonempty network list may still be returned from the Windows cache.
- **OLD SCAN (BSS cache)** means that the last confirmed scan completion exceeds
  the application's conservative age threshold. It does not mean the actual
  networks have disappeared.
- **FALLBACK: last nonempty BSS list** means the app is displaying its own
  retained list because Windows returned no BSS entries on the latest read.
  These retained observations are for display, not new survey measurements.
- **Connected AP not in BSS list** means the current association was identified
  by Windows, but the corresponding BSSID was missing from the discovered BSS
  list. The connection's own SSID, RSSI and PHY rates may still be available;
  channel and band remain unknown rather than being inferred from a neighbor.

## 6 GHz channel numbering

Normal 6 GHz numbering uses channel 1 at 5955 MHz and increments the channel
number by one for each additional 5 MHz of center frequency. 5935 MHz is the
special case for channel 2. A channel number alone must not be confused with
its channel width.
