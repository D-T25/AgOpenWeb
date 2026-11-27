# Implement draft simulation

This is a separate test suite. It runs the **actual production TrackGuidanceService** against an independent physical/sensor model. It does not change production steering, add calibration, or enable U-turns.

## Run and view

Download/extract the Windows draft-test bundle and double-click **Run-Draft-Tests.cmd**. It includes .NET and needs no tractor, GPS, Node, Visual Studio or normal AgOpenWeb installation. The animation opens in your browser after the tests finish. **Exit code 1 means an accuracy/model check failed; read the report.** It does not mean the harness crashed.

From a source checkout with .NET 10:

```powershell
dotnet build Simulators/AgOpenWeb.PassiveHarness -m:1 -nr:false
dotnet Simulators/AgOpenWeb.PassiveHarness/bin/Debug/net10.0/AgOpenWeb.PassiveHarness.dll --draft --output draft-results
```

The HTML is self-contained and works offline. Select straight/curve cases, press Play, scrub time, or disable Follow tractor to see the full recorded path. The cyan dot is the controller's measured working-bar position; the blue bar is ground truth. Force arrows are exaggerated, labelled lateral components—not distances or whole force vectors.

To change inputs, copy `draft-settings.json`, edit it, then use:

```powershell
AgOpenWeb.PassiveHarness.exe --draft --config my-draft-settings.json --output my-results
```

The old kinematic/replay suite remains available without `--draft`. Its results and acceptance limits are unchanged.

## Independent model and units

The illustrative implement is **8 m wide, 2,500 kg, with an 8 m drawbar**, a tractor hitch 1.5 m behind the axle, and the working bar 1.5 m behind the implement axle. These are test assumptions, **not measurements of the user's implement**. Each seed varies implement mass ±10% and tyre/soil cornering stiffness ±20%. All effective values are stored in each run's setup.

- Physics: 100 Hz, guidance: 10 Hz.
- Tractor: bicycle steering, 0.35 s steering lag, 30°/s steering-rate cap, lateral slip relaxation and yaw response to hitch reaction. Longitudinal speed reduction in soil patches is a simplified governor/load proxy, not an engine/powertrain model.
- Implement: rigid planar trailer, mass and yaw inertia, constrained hitch/axle geometry. No production ToolPositionService or passive geometry generates the feedback.
- Lateral ground resistance: `Fsoil = -Fmax*tanh(Cα*α/Fmax)`, with slip angle from actual ground velocity and `Fmax = μ*m*g*cos(slope)*gripFactor`. Default `Cα=24,000 N/rad`, `μ=0.45`; coefficients are illustrative.
- Asymmetric draft: default longitudinal draft 30,000 N and 2% asymmetric lateral component at the working bar. Soil patches change grip, draft and asymmetry. Gravity acts downhill in the field frame, projected into the implement's lateral axis.
- A finite-travel, finite-rate hitch play/compliance proxy (default ±25 mm) allows displacement under changing hitch load. This is not a pin-impact/contact model.
- GNSS: default implement 5 Hz / 150 ms delay; tractor 100 ms delay; 1.5 cm Gaussian implement position noise, smaller tractor position noise, 1 cm slow implement bias, 0.15° heading noise and 0.1° measured roll noise. Antenna height 2 m, physical slope-induced antenna shift and antenna-to-working-bar distance are modelled.
- Dual GNSS supplies body orientation. IMU cases include relative yaw bias and slow drift; single-antenna course comes from ground motion, not body heading. Position-only cases cannot invent roll compensation. RTK-outage cases stop new implement fixes and use the production stale-data gate.

For hitch-to-axle distance `L`, rear working-bar distance `W`, axle-centred inertia `I`, hitch lateral acceleration `Ah`, and positive-right forces, eliminating the hitch lateral reaction gives:

```
(I + m*L²) * betaAcceleration = m*L*Ah - L*(Fsoil + Fgravity) - (L+W)*FasymmetricDraft
```

The plant also computes the hitch reaction and feeds its lateral/yaw effect back to the tractor. The two-way coupling is a reduced yaw-plane model: it is not a full four-wheel vehicle, full soil-cutting model, rutting model or three-dimensional suspension/rollover model.

## Validity checks and reporting

Independent checks verify zero-force straight invariance, left/right force symmetry, yaw damping, an analytic steady side-force balance with a rigid tractor fixture, and a 10 ms versus 5 ms closed-loop timestep comparison. Each trajectory checks finite results, full duration, configured steering/offset bounds, friction usage, hitch-geometry consistency and maximum articulation domain (70°). Those are **model-domain checks**, not a field safety certificate.

Each case runs passive OFF and ON with matched seed/physical parameters. Three seeds and 31 conditions produce **186 trajectories**. Normal-case acceptance is still ≤20 cm straight or ≤30 cm curve RMS **and** ≥30% RMS reduction against passive off. Stress cases are characterized without asserting that tracking is acceptable. The harness preserves every failure.

Metrics use true bar error, not noisy measured error: evaluate 30 s to end, except entry turns which use 25–70 s. Reports include RMS, P95, peak, signed mean error, standard deviation, line crossings (5 cm hysteresis), offset-cap time, grip-saturation time and engagement. Animations show seed 1; all seeds are in the table and raw CSV.

Outputs: `draft-simulation.html`, `draft-results.json`, `draft-samples.csv`, and editable `draft-settings.json`. The HTML replays recorded data; it contains no guidance controller.

## What is needed to match a real implement

Actual implement type, width, mass/axle loading, hitch/drawbar/bar/antenna dimensions, GNSS rate/delay, speed, slope and logged response are needed. Measured side draft or hitch load would help identify grip/cornering coefficients. Until identified from field data, these tests expose weaknesses and compare scenarios; their centimetre results do not predict field accuracy.

Primary background on planar force balances and nonlinear lateral tyre forces:

- https://www.mdpi.com/2624-7402/6/4/206
- https://eprints.lib.hokudai.ac.jp/repo/huscap/all/13144/68%281%29_p1-16.pdf

The harness derives its own reduced equations; it does not copy their fitted coefficients or reproduce their complete models.
