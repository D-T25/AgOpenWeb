#!/usr/bin/env python3
"""Audit and explain a completed setting sweep plus its optional follow-up runs."""
import json
import sys
from pathlib import Path
from statistics import mean

root = Path(sys.argv[1])
data = json.loads((root / 'results.json').read_text())
runs, findings = data['runs'], data['findings']
comparisons = []
for motion, preview in [('150', '0.5'), ('200', '1')]:
    for r in runs:
        if r['control'] == 'Motion Sensitivity' and r['value'] == motion:
            q = next(q for q in runs if q['scenario'] == r['scenario'] and q['seed'] == r['seed']
                     and q['control'] == 'Tool Look-ahead (s)' and q['value'] == preview)
            comparisons.append(max(abs(q[k] - r[k]) for k in ('rmsM', 'weaveM', 'biasM', 'steerRmsDegrees')))
assert len(comparisons) == 2 * len({r['scenario'] for r in runs}) * len({r['seed'] for r in runs})
assert max(comparisons) < 1e-9, 'Equivalent Motion/preview settings diverged'

def row(scenario, control='Baseline', value=None):
    return next(r for r in findings if scenario in r['scenario'] and r['control'] == control
                and (value is None or r['value'] == value))

def cm(value):
    return f'{value * 100:.1f}'

summary = []
for scenario in dict.fromkeys(r['scenario'] for r in findings):
    b, off = row(scenario), row(scenario, 'Passive OFF')
    summary.append(f"| {scenario} | {cm(off['rmsM'])} | {cm(b['rmsM'])} | {cm(b['weaveM'])} |")

text = '''# Passive guidance: field adjustment findings

These tests support your observation that constant correction can start weaving.
They do **not** establish a finished field setup or inch accuracy. All ground is
level; side draft is a real force in the independent test plant, not a fake GPS
position shift. Production steering and installed profiles were not changed.

## What ran

735 trajectories: 34 settings (baseline plus 33 single-control variations),
seven scenarios, three matched seeds, 180 seconds each. Every setting uses
identical disturbances and noise for a given seed. 14 extra trajectories check
physics convergence. All 735 completed within the existing model domain;
10/10 experiment/domain checks passed. That is not an accuracy pass claim.
The original kinematic/replay regression remains 74/74.

Illustrative implement: 8 m wide, 2,500 kg, hitch-to-axle 8 m, bar 1.5 m
behind axle. Speed 7.2 km/h. These are not measured Great Plains, Monosem or
Harriston profiles. Seed variation: mass ±10%, cornering resistance ±20%.

Baseline: Tracking/Motion/Acquire/Strength/Curve Response 100; interval 4 s;
look-ahead 0; straight and curve offset limits 1 m; early retry on; geometry on.
GNSS: 5 Hz tool position, 150 ms delay, 1.5 cm position noise, 1 cm slow bias,
0.15 degree heading noise. Geometry/antenna installation is assumed correct.

## Baseline results

Three-seed averages. Weave is error standard deviation, which also includes
responses to deliberately changing draft; it is not a pure oscillation score.
All error figures here are centimetres. RMS uses 30 s onward; inspect the trace
for initial engagement. 2.54 cm = 1 inch.

| Scenario | Passive OFF RMS | Baseline RMS | Error variation SD |
|---|---:|---:|---:|
'''+ '\n'.join(summary)+'''

The no-draft straight is a negative control: there is almost nothing to correct.
Its small added error shows some noise chasing. The left curve actually becomes
worse than passive OFF under the nominal noise/draft combination. Do not judge
curve performance from the right turn alone.

## What each adjustment did

All comparisons below change one setting from baseline. They have not been
combined into an optimized profile.

| Adjustment | Intended action in current code | What these runs showed |
|---|---|---|
| Tracking Sensitivity | Increases error-driven offset buildup | 50 reduced steady-draft RMS to 9.9 cm; 200 increased it to 14.6 cm. But 200 helped alternating draft. More is not always better. |
| Motion Sensitivity | Adds error-rate compensation and earlier approach braking | Increasing it did not cure steady-draft weaving. Its action overlaps exactly with Look-ahead. |
| Acquire Sensitivity | Increases demand when error is large | Raising it generally worsened steady-draft results. Check the engagement trace separately: the tractor must first meet the existing acquisition gate. |
| Correction Strength | Limits correction increment and applied offset slew | 50 reduced steady RMS from 11.2 to 8.9 cm and variation from 5.5 to 3.8 cm. This was the clearest gentler-correction result; see finer-step check below. |
| Correction Interval | Sets correction rhythm, with early-retry/release exceptions | 0 s nearly doubled steady-draft variation (5.5 to 10.6 cm), while alternating-draft RMS improved (11.9 to 10.3 cm). A longer hold is a tradeoff. |
| Straight Maximum Offset | Permits farther tractor displacement; does not set response gain | Raising it to 2 or 3 m did nothing in these straights. Even the 0.5 m case was not capped. A larger limit cannot fix an error if the limit is not reached. |
| Curve Maximum Offset | Caps the available displacement on curves | Increasing it above 1 m gave no benefit here. A smaller cap sometimes accidentally helped by limiting overcorrection; do not use that as a geometry fix. |
| Curve Response | Scales geometric curve compensation | 100 worked well on the right curve. Lowering it helped the problematic left curve but made the right curve worse. A single curve slider did not resolve both directions. |
| Tool Look-ahead | Adds error-rate compensation and approach braking time | Functionally duplicates part of Motion Sensitivity. Raising both can add more compensation than intended. |
| Retry early | Allows another correction before the interval if not improving | Turning it off worsened alternating draft, but helped the difficult left curve. This interaction needs controller work, not a universal on/off prescription. |
| Geometry compensation | Adds predicted geometric curve offset | Needed for good right-curve results here; disabling it helped the problematic left/noise case. Exact straights were unchanged. |

The effective limit blends from the straight limit at 200 m radius to the curve
limit at 25 m radius. Both limits therefore affect a 40 m curve.

## Duplicate control verified

Motion 150 with look-ahead 0 matches Motion 100 with look-ahead 0.5 s.
Motion 200 with look-ahead 0 matches Motion 100 with look-ahead 1 s.
The controller uses the sum `Motion / 100 + LookAhead` in both rate compensation
and approach braking. These should eventually become one understandable control.
'''
text += f'Across {len(comparisons)} matched pairs, the maximum audited metric difference was {max(comparisons):.3g}; the curves are effectively identical.\n'
audit = {'motionPreviewPairs': len(comparisons), 'maximumMetricDifference': max(comparisons)}
fine_path = root / 'Fine-Step-Checks/draft-results.json'
if fine_path.exists():
    fine = json.loads(fine_path.read_text())
    groups = {}
    for r in fine['runs']:
        if r['passive']:
            groups.setdefault(r['name'], []).append(r)
    text += '''
## Finer physics step: did the important findings persist?

48 additional trajectories at 5 ms instead of 10 ms; three matched seeds.
53/53 existing draft-suite checks passed. Values are three-seed averages.
Small ranking differences can move with timestep and noise; do not treat the
lowest decimal as a field optimum.

| Scenario / single change | RMS cm | Variation SD cm |
|---|---:|---:|
'''
    for name, group in groups.items():
        text += f"| {name} | {cm(mean(r['rmsM'] for r in group))} | {cm(mean(r['stdDevM'] for r in group))} |\n"
    text += '\nThe main steady-draft findings persisted: gentler Strength reduced variation; continuous corrections increased it. The benefit of Strength 50 after a draft reversal was much smaller at the finer timestep.\n'
    audit['fineStepPassed'] = sum(c['pass'] for c in fine['checks'])
    audit['fineStepChecks'] = len(fine['checks'])
mirror_path = root / 'Mirror-Checks/draft-results.json'
if mirror_path.exists():
    mirror = json.loads(mirror_path.read_text())
    on = {r['name']: r for r in mirror['runs'] if r['passive']}
    deltas = [abs(on[a]['rmsM'] - on[b]['rmsM']) for a,b in [
        ('Right / zero asymmetric draft', 'Left / zero asymmetric draft'),
        ('Right / negative draft', 'Left / positive draft')]]
    assert max(deltas) < 1e-9
    text += '''
## Curve mirror and sensor check

Ten additional trajectories remove position/heading/roll noise and slow bias
while retaining the same GNSS rate and delay. Mirrored no-draft turns matched
at 1.54 cm RMS; mirrored turns with reversed draft matched at 4.31 cm RMS.
That supports the basic mirror geometry in this diagnostic; it does not prove
that every production sign/state transition is correct.

For seed 1, the left curve with positive draft changed from 26.26 cm RMS with
nominal sensors to 4.31 cm with idealized sensors. The model and draft were
otherwise identical. This exposes a substantial sensitivity to the combined
sensor noise/bias inputs; this test does not isolate which sensor component or
controller rule causes it.

The diagnostic report preserves a failure: **14/15 checks passed**. The remaining
check asks for a 30% improvement on an already nearly perfect no-draft straight
(OFF effectively zero; ON about 0.032 cm RMS). It fails the relative-improvement
rule despite negligible absolute error. All ten trajectories completed within
bounds. Acceptance was not loosened to make this diagnostic appear to pass.
'''
    audit['mirrorRmsDifferencesM'] = deltas
    audit['mirrorPassed'] = sum(c['pass'] for c in mirror['checks'])
    audit['mirrorChecks'] = len(mirror['checks'])
text += '''
## What I would do next in the field

1. Get tractor-only steering smooth at working speed and depth, with passive off.
2. Enter measured hitch-to-axle, axle-to-row/bar and antenna dimensions. Verify
   tool position, heading/roll orientation, fix quality, update rate and delay.
   Antenna height, lateral/mounting offsets and roll zero are measurements, not
   gain knobs. Check hitch play and antenna/frame movement under load.
3. Save a reference profile. On a long flat straight with steady draft, compare
   Strength 100 against 50, leaving every other setting unchanged. Repeat in
   both travel directions. Compare actual row position and steering movement,
   not just a noisy screen error. These are candidate trials, not validated
   settings for any of your three implements.
4. Test Tracking separately and retain enough hold for the implement to respond.
   Do not raise Tracking, Strength and correction frequency together. Leave
   Look-ahead at zero while evaluating Motion because their actions overlap.
5. Inspect the live offset/cap status before increasing the offset limit. More
   allowance helps only when the required offset is being restricted.
6. Recheck changing soil/draft and engagement. A setup that looks smooth under
   constant pull can lag when the pull changes sides.
7. Finally check left and right curves. The current noisy-sensor curve result
   needs improvement before calling this a finished setup.

## Software direction

The overall method is appropriate: geometric curve compensation plus measured
working-bar GPS feedback, backed by independent simulation and real field logs.
The present results show that slider tuning alone is not a complete solution.

Next controller investigation should separate the sustained tractor offset
needed to counter draft from temporary acquisition/braking corrections, and
examine noise handling, early retry and approach release together. Repeated
approach-release states occur in steady draft, but their presence does not
establish the cause. Use a controlled ablation before changing that rule.
Consolidate the duplicate Motion/Look-ahead adjustment after preserving settings
migration. Validate any change against straight stability, changing draft,
engagement and both curve directions. Machine learning is not needed to resolve
these basic feedback and measurement issues.

Actual drawbar/working-bar dimensions and loaded operating weights are still
needed for the Great Plains, custom Monosem bar and Harriston. Width alone is
not enough. Field logs should retain tractor/tool positions and headings,
RTK quality and timestamps, speed, requested/applied offset, controller reason
and the measured row error. The model's coefficients remain illustrative.

No automatic calibration feature, U-turn change or new production steering
algorithm is included in this test package. Windows harness packaging is
cross-compiled; native Windows launching has not been verified here.
'''
(root / 'FIELD-FINDINGS.md').write_text(text)
(root / 'analysis-checks.json').write_text(json.dumps(audit, indent=2))
print(json.dumps(audit))
