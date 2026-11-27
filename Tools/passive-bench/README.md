# Passive guidance test harnesses

These exercise the actual shared AgOpenWeb guidance code. They add no calibration and do not change U-turn planning or production steering settings.

## 1. Offline coupled driving and replay harness

A standalone .NET console program at `Simulators/AgOpenWeb.PassiveHarness`. The Windows download includes .NET: extract it and double-click **Run-Harness.cmd**. It runs without AgOpenWeb, tool hardware, Node or Visual Studio. It never starts a network listener or sends steering packets.

Each driving scenario runs for 150 simulated seconds at 10 Hz, once with passive off and once on. The tractor uses bicycle kinematics with a 0.35-second steering lag and a 30°/s steering rate limit. A separately implemented trailer model uses the hitch velocity projected onto the trailer's lateral axis. It does **not** use the production `ToolPositionService` to generate feedback. Analytic circle identities cross-check baseline off-tracking.

The tool is 24.3 m wide with an 8 m drawbar and a hitch 1.5 m behind the axle unless the scenario explicitly overrides them. Most curve scenarios use a **2 m offset limit**; separate cases exercise the app's 1 m default and a saturated long trailer. These are declared test settings, not recommended field settings. Every run records its complete scenario settings.

Scenarios cover straight lines in both directions, dual heading, position-derived heading, single GNSS plus IMU, 2 cm measurement noise, continuous/held corrections, GPS outage/rearm, left/right circles, straight–arc–straight entries, an S-curve, slow/fast driving, and saturation stress. Deterministic replays also test reset gates, acquisition, early retry, antenna offsets and receiver parsing.

Outputs:

- `report.html`: interactive comparison plots, settings and check details; works offline.
- `results.json`: checks, metrics and samples.
- `samples.csv`: 100 ms samples for independent analysis.

RMS is measured over seconds 100–150, except entry turns which use 25–70 seconds to include the arc and transitions. Predeclared tracking acceptance requires ≤20 cm on straight lines or ≤30 cm on curves **and** ≥30% reduction over passive off. Stress cases verify finite/bounded behavior rather than enforce a field accuracy target. A failed acceptance returns exit code 1; it does not mean the harness crashed. The report preserves every failure; thresholds are not relaxed after a run.

Build from source (.NET 10 SDK):

```powershell
dotnet build Simulators/AgOpenWeb.PassiveHarness -m:1 -nr:false -p:DesktopOnly=true
dotnet Simulators/AgOpenWeb.PassiveHarness/bin/Debug/net10.0/AgOpenWeb.PassiveHarness.dll --output passive-results
```

The synthetic side-draft term deliberately allows trailer body heading to differ from its direction of travel. It is a simplified disturbance, **not a validated soil model**. It tests whether orientation can contaminate lateral-motion damping; the current controller derives that motion from GPS path-error changes. Geometry, reverse steering forces, tyre slip, GNSS correlation, roll dynamics and hardware latency need further validation before predicting field accuracy. Reverse acquisition is covered by the existing guidance tests; this harness does not certify passive or active corner backing.

## 2. Live simulator / web / UDP harness

`browser.cjs` starts the real desktop backend with an isolated temporary profile and drives the real web client using Playwright. It refuses to attach if localhost port 5174 is occupied. Close normal AgOpenWeb first. It never edits your normal profiles.

Requirements: Node.js, Playwright/Chromium and a built desktop head. Install once in this folder:

```powershell
npm install
npx playwright install chromium
```

Run against an extracted Windows app:

```powershell
node browser.cjs --head "C:\AgOpenWeb\AgOpenWeb.Desktop.exe" --output browser-results
```

Or a developer DLL:

```powershell
node browser.cjs --head ../../Platforms/AgOpenWeb.Desktop/bin/Debug/net10.0/AgOpenWeb.Desktop.dll --output browser-results
```

`--dotnet PATH` selects a runtime for a DLL; `--browser PATH` selects an already installed Chromium executable. All commands go to the locally launched host, and the only synthetic receiver datagrams go to **127.0.0.1:19999**. Tests use the built-in tractor simulator. Do not run alongside a field session.

The 16 checks cover default-off behavior, independent Tool GPS/request/tractor gates, fixed/trailing engagement, a real nonzero correction, section controls, passive disarm, tool-source toggling, real implement PAOGI over UDP with simulator Tool GPS off, stale-data shutdown, observer rejection and authority-loss reset. Results are saved as HTML/JSON beside `host.log` and `transitions.json`. State transitions distinguish operator/control resets from section changes. The extra checks exercise 12 live section changes during trailing correction and deliberately freeze the operator UI for 2.1 seconds to verify deadman shutdown. `node client-scheduling.cjs` checks that GPU and 2D coverage queues yield without dropping or duplicating cells. The host is stopped on completion; its isolated temporary data directory is retained for diagnosis and printed in JSON.

## Scope

These tests exercise the shared backend and web interface. They do not launch a native Windows WebView2 shell, connect physical equipment, validate full implement footprints/headland coverage, or test active implement actuator steering. A Linux run cannot certify Windows-native operation. The downloadable Windows offline harness has been cross-compiled, not launched on Windows here.

## Force-based draft tests

For straight/curve draft forces, side slope, soil grip, hitch reaction and delayed/noisy GNSS, see [DRAFT-MODEL.md](DRAFT-MODEL.md) and [DRAFT-FINDINGS.md](DRAFT-FINDINGS.md). Run the new suite with `--draft`; its animated report is separate from the original kinematic/replay report.
