# Twol passive implement guidance port

This branch ports the **manual passive guidance** from D-T25/Twol commit `cce4944fd60676525e8b802750510c757520c188`. It has no passive calibration or automatic tuning. Existing AgOpenWeb profiles default to passive disabled.

## Setup and use

1. Tune tractor guidance normally. Passive correction currently requires **Pure Pursuit**, not Stanley. Select an AB line or curve; contour guidance is not supported by this port.
2. In **Tool Configuration → Passive**, enable passive mode. Verify the existing tool width, tractor hitch, implement drawbar/tank drawbar and pivot-to-working-bar dimensions. Enable **Use verified profile geometry** only when those measurements and attachment type are correct. Articulated tractor geometry and front-mounted trailing geometry are unsupported; they block geometric correction with an explanation.
3. Enter implement antenna height, lateral mounting offset and the signed distance from antenna to working point. A positive fore/aft distance means the antenna is ahead of the working point. Positive lateral offset follows Twol's convention: the working point is right of the antenna (antenna mounted left). Check heading mounting offset and roll zero/inversion. Distances are metres and angles are degrees.
4. Supply implement GPS separately via **UDP 19999** (the listener runs when passive mode is enabled). Twol's existing ToolDual firmware sends PANDA/PAOGI to this port. Stop Twol if it is also running on the same computer and holding this port. Tractor GPS stays on AgOpenWeb's normal input. The implement receiver never publishes to the tractor GPS stream or changes tractor heading/fix quality.
5. Engage tractor autosteer, then press the separate **Tool** button on the right or **Passive on/off** in the Passive tab. Both requests are required. Section application/painting does not enable, disable or suspend passive guidance. Loss of web control authority disarms the implement request along with tractor autosteer.
6. Save using the existing **Save tool profile** action. Settings live with the implement, not the tractor. The armed button state is runtime-only and is not saved.

The cyan map marker shows the measured working point while passive measurement is valid; the existing implement body/coverage geometry remains predicted. The Passive tab shows actual implement working-point error, applied tractor offset, heading source and the current engagement/waiting reason. The normal tractor line/XTE remain the reference; passive correction shifts only the Pure Pursuit goal normal to the path at the goal. It does not nudge the stored track or alter pass numbers.

## Manual controls

| Control | Effect |
|---|---|
| Tracking Sensitivity | Response to implement cross-track error; 50–200. |
| Motion Sensitivity | Damping/preview from changes in GPS working-bar error, independent of body yaw; 50–200. Saved under the existing `Heading` profile key. |
| Acquire Sensitivity | Extra correction away from the line; does not change engagement thresholds. |
| Curve Response | One 0–300% geometry multiplier; 100% uses measured dimensions. No second curve-gain/sensitivity control. |
| Correction Interval | 0–25 seconds; default 4, zero means continuous. |
| Correction Strength | Step and slew limit; at 100, up to 0.20 m/correction and 0.20 m/s. |
| Straight / Curve Maximum Offset | Separate 0.1–3 m limits, smoothly blended with curvature. |
| Tool Look-ahead | Optional 0–2 seconds of additional heading preview. |
| Retry early | Retry when off-line and not improving after the minimum response delay; keep holding while returning. |

Without **Use verified profile geometry**, guidance is feedback-only. AgOpenWeb has no Twol legacy curve-base value to preserve. Start from 100% sensitivities and strength, 4-second interval, 1 m limits and zero extra look-ahead, then adjust one control at a time. Previously calibrated Twol values can be entered manually; this branch does not import Twol XML profiles.

## Receiver and heading support

The dedicated receiver accepts checksum-validated whole PANDA/PAOGI sentences. PAOGI heading/roll use decimal degrees; PANDA IMU heading/roll use tenths of a degree and accept the 65535 no-IMU sentinel. Optional ToolDual fields 16–19 carry heading source, validity, sensor age and GNSS course. Invalid, stale or absent heading does not become a false north heading.

Heading priority is dual baseline, GNSS-anchored relative IMU, GNSS course, then displacement-derived course. At insufficient motion, position-only feedback uses the antenna point. No IMU is required. Reverse state is used to distinguish course from body heading. This is passive tractor-target correction; it adds no active implement actuator/reverse controller.

A fresh implement fix (up to 1.5 seconds) is required. Low speed (<2 km/h), missing/invalid fix, inactive steering request or U-turn execution clears the applied offset. Initial acquisition requires the tractor within 10 cm and 1.5 degrees of its reference. The existing tractor GPS/steering safety gates remain in force.

Provide appropriate RTK corrections to both receivers. The receiver addition does not change AgOpenWeb's NTRIP routing: if the tractor currently receives unicast corrections, arrange implement corrections separately or use the existing broadcast option when appropriate for the network. The firmware has not been changed in this port.

## Built-in simulator

Enable the simulator, open or create a field, and select an AB line or curve. On the bottom simulator bar, turn **Tool GPS** on. This supplies an independent virtual implement antenna from the existing trailing-tool model; no UDP tool receiver is needed. Set the tool type and hitch/drawbar dimensions in the tool profile before testing.

Enable passive mode in Tool Config → Passive, drive above 2 km/h, engage tractor autosteer, then turn **Passive on**. The tractor must first acquire the line within 10 cm and 1.5°. The passive panel identifies the source as **Simulator**. Sections do not affect engagement. Tool GPS does not arm steering by itself.

Turn Tool GPS off to return to the real implement receiver. Simulated tool input defaults off at startup and switches off when tractor input leaves the built-in simulator. Changing input sources resets passive corrections. This geometric simulation exercises guidance and UI integration; it does not simulate soil forces or side-draft.

## Architecture and validation

All runtime code is under `Shared/`. UDP reception parses into an independent immutable implement measurement. The cycle worker converts it into the field's local coordinate system, applies drift consistently, and runs passive control inside the shared track guidance service. A pipeline intent owns the separate armed request; snapshots carry telemetry to the UI mirror and binary Tick/Config extensions carry it to the browser. `passive.*` commands require fresh control authority; `config.set` cannot bypass that gate. Settings are validated at the profile boundary.

Twol's controller, heading resolver and curve geometry are retained with GPL attribution. The wrapper adapts AgOpenWeb coordinate types and shifts the existing PP goal before steering is calculated. U-turn planning, boundary planning, track generation and section control are unchanged.

Focused tests cover live PP offset direction in both track directions, gating, stale/invalid fixes, no effect when disabled, bounded slew, heading fallback, geometry, ToolDual decoding, tool-profile persistence and last-wins intent delivery. Run:

```sh
dotnet test Tests/AgOpenWeb.Services.Tests --filter FullyQualifiedName~PassiveGuidanceTests
dotnet build Platforms/AgOpenWeb.Desktop -p:DesktopOnly=true
python3 Tools/i18n-extract.py --check
```

This is an experimental port for simulator/bench validation. Host compilation and automated tests do not establish field performance, and iOS/Android native packaging requires their platform toolchains.

Validation covers 167 model tests, 1,578 service tests, 319 ViewModel tests and four remote-authority tests (two service tests skipped), plus deterministic client scheduling tests and the coupled driving/live browser harnesses. The live checks cover fixed/trailing engagement, real loopback implement UDP, section independence, observer rejection, disconnection and frozen-page shutdown. These results do not establish field performance or native Windows/mobile execution.

## Motion feedback and operator responsiveness update

Orientation (dual GNSS, GNSS-referenced IMU, or movement-derived heading) positions the antenna and working bar. Damping, approach braking and look-ahead use the filtered rate of change of the GPS working-bar path error. A sideways-pointing tool travelling parallel to the line no longer invents lateral velocity. The existing heading slider is now labelled **Motion Sensitivity**, and old profile values are retained. Straight and curve offset limits remain manual; saturation reads **Offset limit reached**.

The browser slices coverage rasterization into 6 ms budgets (plus its existing cell cap), releases cached GPU snapshots before writing, and pulses operator presence from the UI timer and before rendering. The pulse rate is limited to once per 250 ms. The server's **1.5-second deadline is unchanged**. No worker renews a frozen page. A late heartbeat cannot revive an expired hold before the next sweep. New deterministic authority and renderer tests, plus a deliberate 2.1-second page freeze in the live bench, verify the shutdown path.
