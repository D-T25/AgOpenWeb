# First harness findings — 2026-11-27

Production code under test: `eb796df61d5db0101ed2f4271fac849c01233550`, branch `feature/twol-passive-guidance`. Harness development does not change production guidance.

## Offline coupled model

**69 of 74 checks pass. Five declared tracking acceptance checks fail.** All reset/bounds/receiver/hold/geometry contract checks pass. Twenty-one paired driving scenarios produce 42 trajectories and 63,000 samples.

- Synthetic straight side-draft: dual heading, opposite travel, IMU-assisted, noise and continuous-correction cases leave 20.3–20.7 cm RMS, versus 40 cm with passive off. They improve tracking but fail the 20 cm limit. Position-derived heading leaves about 0.7 cm in this particular model. This is **not evidence that a single antenna is generally better**: the artificial disturbance intentionally skews body heading relative to velocity, exposing the controller's heading-as-lateral-velocity assumption. That assumption deserves field logging and further bench work.
- Without the side-draft disturbance, the straight model tracks within about 0.7 cm RMS.
- Steady 25 m and 40 m radius curves track within about 0.2–1.4 cm with the declared 2 m limit. The 25 m curve with the default 1 m limit leaves 16.3 cm RMS. These are model results, not achievable RTK accuracy claims.
- Straight–arc–straight entries leave 7.8 cm RMS during the turn window; the S-curve leaves 6.7 cm RMS.
- A 12 m trailer on a 20 m radius curve with only 1 m permitted tractor offset leaves 2.61 m RMS. It stays bounded, but the allowed offset is physically insufficient in this model. A passing bounds check does not mean acceptable tracking.

Acceptance limits were retained throughout: ≤20 cm straight or ≤30 cm curve and at least 30% improvement over baseline. No thresholds were relaxed to make this report pass.

## Live simulator / web / UDP

One complete isolated run passes all **14 checks**: independent controls, fixed/trailing engagement, nonzero correction, section independence, request/source disarm, real loopback implement PAOGI and stale-fix shutdown, observer rejection and authority-loss reset. Subsequent runs expose an intermittent authority-loss problem; see the final browser report and archived retries rather than treating the single passing run as proof of stability.

Additional runs timed out in the section check, including a run without concurrent builds. The recorded state trace shows operator control becoming false before the tractor disarms and passive request clears, followed by control reacquisition. That is consistent with the existing 1.5-second presence deadman; no section command calls the passive request API. This does not establish which resource or delay caused the presence loss. A separate earlier run saw the same request-reset symptom without the detailed trace. These failures are retained rather than discarded. The harness keeps the failed check and runs potentially disruptive section/coverage updates last on a new operator connection so earlier tests are not hidden. The final reordered run passes all 14 checks, but does not erase the earlier failures or establish their root cause. UI/heartbeat behavior during section/coverage updates needs investigation; it is not established that concurrent builds are required to reproduce it.

## Next work suggested by evidence

1. Log both body heading and displacement-derived lateral velocity during side-draft; compare their disagreement before changing heading damping.
2. Investigate UI/heartbeat latency under load; preserve deadman shutdown behavior.
3. Expand transient/soil cases against measured field logs before using these numerical errors to set field defaults.

The harnesses do not certify native Windows launch, implement actuation, corner backing, or boundary/coverage planning. No calibration feature was added.
