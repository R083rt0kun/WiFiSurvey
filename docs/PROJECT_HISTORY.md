# Wi-Fi Survey — Public Development History

**Scope:** This document describes the source and development history included in the public Wi-Fi Survey snapshot. The final functional development milestone in this snapshot is **PR #2**, merged on **October 20, 2026, at 07:09:42 UTC**.

**Source baseline:** `94a408119a9dbddb2b24008eebe0a816dceb5126` (merge commit of PR #2). Its Git tree is `1fa724466dc779398849d2bb9ec4a95a988d0b4a`. This baseline contains 24 tracked files and 33 reachable Git commits, including the original project's ancestry. Any documentation-only commit used to publish this historical description does not change the selected functional source baseline.

## Origin and authorship

Wi-Fi Survey derives from [CosmicBytez/WifiSurvey](https://github.com/CosmicBytez/WifiSurvey), originally developed by CosmicBytez. The earliest retained project commit is dated **January 22, 2026**, and its message describes the initial WifiSurvey site-survey release. The original `LICENSE` contains the MIT license and the notice **Copyright (c) 2025 CosmicBytez**. The 2025 date is the preserved copyright notice; it is not represented here as the date of the first retained Git commit.

The independent changes represented in this public snapshot were made under the Git identity **R083rt0kun** in 2026. The original copyright notice, relevant source attribution, and historical author/commit metadata are retained. This is an independently published derivative, not an official CosmicBytez release or an endorsement by the original author.

## Development milestones

| Date (UTC) | Commit / milestone | Description |
| --- | --- | --- |
| 2026-01-22 | `c7ea4502` | Initial WifiSurvey site survey tool: floor plans, Wi-Fi measurements, heatmaps, project files, and exports |
| 2026-03-29 | `8093cc7a` | MIT license added to the retained Git history |
| 2026-07 to 2026-10-18 | Maintenance commits | Dependency maintenance, Renovate configuration, build/test CI, and a tag-based Windows packaging workflow |
| 2026-10-20 00:51 | `1d6bf468` | Fixed a startup crash related to `SplitContainer` layout handlers |
| 2026-10-20 01:24 | `6fc9e31f` | Corrected detection of the connected access point using its actual BSSID |
| 2026-10-20 01:25 | `e4517ad4` | Started Live Monitor automatically when the main window opens |
| 2026-10-20 02:06 | `21e7ed61` | Improved Wi-Fi scanning reliability |
| 2026-10-20 02:15 | `cd770765` | Added a BSSID column to Live Monitor |
| 2026-10-20 03:22 | `7ea22195` | Corrected PHY link rates and improved signal diagnostics |
| 2026-10-20 03:57 | `922a7c1c` | Fixed RSSI history timing and roaming tracking |
| 2026-10-20 04:01 | `f1101494` | Improved scanner accuracy and scan status reporting |
| 2026-10-20 04:48–05:26 | Attribution/configuration commits | Clarified project origin, independent attribution, window title, and Renovate configuration |
| 2026-10-20 06:58 | PR #1 · `349e4280` | Hardened Release CI: dry-run packaging, read-only build permissions, version and SHA-256 checks; no production release created by this PR |
| 2026-10-20 07:09 | **PR #2 · `94a40811`** | Normalized WiFi project/solution names, C# identifiers and namespaces, test paths, CI script references, and user-facing Wi-Fi wording; retained `.wifisurvey` compatibility and MIT attribution |

## What the selected public source includes

The selected snapshot contains the Windows/.NET 8 WinForms application, floor-plan and site-survey functionality, Native Wi-Fi scanning, its then-current Live Monitor, RSSI/connected-access-point improvements, heatmap generation, project files and exports, unit tests, and build/release workflows. Descriptions of features refer to the source as it stood at the selected baseline and are not claims of independent hardware certification or a production release.

**End of public development history: PR #2 — October 20, 2026.**

## License and verification

See the unmodified root `LICENSE` for the MIT terms and original CosmicBytez copyright notice. See Git commit history for the exact authors, timestamps, source changes, and merge ancestry. This chronology is a readable summary, not a substitute for the retained Git evidence.
