# Passive field adjustment experiment

This suite changes one controller property at a time, using the actual production
TrackGuidanceService and the independent force-based DraftPlant. Production
steering, user profiles and U-turns are unchanged. This is a manual adjustment
study, not an automatic calibration feature.

```powershell
dotnet build Simulators/AgOpenWeb.PassiveHarness -m:1 -nr:false -p:NuGetAudit=false
dotnet Simulators/AgOpenWeb.PassiveHarness/bin/Debug/net10.0/AgOpenWeb.PassiveHarness.dll --settings-sweep --output setting-sweep-results
```

Optional `--seeds 1..20` (default 3), `--duration 120..600` seconds (default 180).
Open `Field-Adjustment-Report.html` after completion. It works offline. Exit 1
means an experiment/domain check failed; exit 2 means a configuration/runtime error.
Accuracy results are not silently converted into passing checks.

## Design

Seven scenarios: no-draft straight, constant side draft, side-draft reversal,
alternating side draft, engagement from 1 m off line, right and left 40 m curves.
Every scenario is level. An asymmetry ramp begins at 20 s and ends at 25 s;
reversal occurs from 65 to 73 s. Alternating asymmetry varies with travelled
distance. These are lateral cutting/draft forces, not an artificial GPS offset.

Baseline: default production controls (100), interval 4 s, look-ahead 0 s,
both limits 1 m, early retry on, with geometry explicitly enabled. 33 variants
plus baseline exercise 11 adjustments. Each seed uses identical noise and plant
parameters across all variants and passive OFF. Three seeds vary mass ±10%
and implement cornering stiffness ±20%. Total: 735 trajectories plus 14
numerical-convergence trajectories. Duration 180 s; 100 Hz plant, 10 Hz guidance.
Every nonbaseline variant is checked to change exactly one property.

The illustrative implement is 8 m wide, 2,500 kg, hitch to axle 8 m, bar 1.5 m
behind axle. Tractor hitch is 1.5 m behind tractor axle; speed 7.2 km/h.
These are **not measured profiles of the user's Great Plains, Monosem or
Harriston**. Antenna dimensions, heading and roll installation values are held
fixed. Those measurements should not be treated as gains to tune away error.

## Reading results

- RMS includes bias and weaving; standard deviation about the signed mean
  separates oscillation from sustained offset. Both matter.
- Evaluate 30 s onward; inspect the full trace for engagement. Late RMS uses
  the final 30 s. Crossings use a ±5 cm hysteresis band.
- Settling: first continuous 10 s inside ±10 cm after time 0 (engagement),
  25 s (draft ramp), or 73 s (draft reversal). This is first settling, not a
  guarantee that the tool remains settled. Alternating draft has no final state.
- Table results average three seeds; plotted traces show seed 1 at 0.5 s
  spacing. Metrics and controller reason counts use the full 0.1 s sequence.
- Offset-limit usage uses the straight limit on exact straights and the
  curvature-blended limit on circles, with a 1 cm threshold. This is an
  approximate occupancy statistic, not the exact internal saturation flag.
- The limits blend from straight at 200 m radius to curve at 25 m radius.
  Therefore the straight limit affects a 40 m curve. Curve-only controls
  (curve response, curve limit, geometry compensation) are checked to leave
  exact straights identical.
- Full duration, finite positions/forces, configured maximum offset bounds,
  steering limits, hitch constraint and articulation domain are checked on
  each trajectory. Each scenario also compares 10 ms and 5 ms physics.

The existing draft-suite tracking target remains 20 cm straight / 30 cm curve
RMS and ≥30% reduction versus passive OFF. This sweep reports observed error;
passing numerical checks is not an accuracy claim or field validation.

Outputs: settings.json (all scenarios and values), results.json (metrics,
controller reasons and seed-1 traces), metrics.csv and the interactive HTML.
The original 74-check kinematic/replay suite remains available without a flag.

## Limits and next field work

The model has no identified soil coefficients, individual row units, frame
flex or suspension. Repeat with measured hitch/working-bar distances, operating
weight, antenna location and field logs before choosing equipment settings.
First validate tractor-only steering, then passive straight rows, then curves.
Save one reference profile, change one control, repeat in both directions at
constant speed/depth/load, and compare tool error as well as steering activity.
Do not assume that the lowest-RMS setting on one simulation is best everywhere.

## Follow-up runs in the delivered example

`fine-step-settings.json` repeats selected steady/alternating/reversal comparisons
at 5 ms with three seeds (48 trajectories, 53/53 checks). `mirror-settings.json`
uses idealized sensors for no-draft/reversed-force mirror checks (10 trajectories,
14/15 checks; the zero-error straight fails the unchanged relative-improvement
rule). Their configs and full results are retained in the example package.
Run either with `--draft --config FILE --output DIRECTORY`, then run:

```bash
python3 Tools/passive-bench/analyze-setting-sweep.py setting-sweep-results
```

The analyzer checks 42 equivalent Motion/Look-ahead pairs and, when present,
mirrored curve RMS. It writes `FIELD-FINDINGS.md` and `analysis-checks.json`.
Its narrative interprets the default 3-seed, 180-second reference experiment;
it rejects different run counts/durations rather than attaching these numerical
examples to another experiment. Other sweeps retain their own dynamic HTML and CSV.
The Windows batch file reruns the primary sweep; it does not rerun these separate
diagnostics. The guide distinguishes the main numerical passes from the retained
mirror-suite acceptance failure and the unsolved noisy curve result.
