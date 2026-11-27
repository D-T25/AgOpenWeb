# Force-based draft test findings — 2026-11-27

Production controller tested: `f520558fbb5cfc83e424a112e9d1a7820f7c3106`. Production steering is unchanged in this test work. See DRAFT-MODEL.md for the model, equations, assumptions and reproduction instructions.

## Coverage

31 conditions × 3 seeds × passive OFF/ON = **186 trajectories**, each 180 simulated seconds: **9.3 hours of simulated driving**. Seeds vary mass ±10% and cornering stiffness ±20%. The prior kinematic/replay suite still passes **74/74**.

The new suite passes **122/173 checks**. All **98 numerical/model/bounds checks pass** (93 paired full-trajectory checks and five independent physical/convergence checks). Of 75 normal-case tracking acceptance checks, **24 pass and 51 fail**. The targets remain ≤20 cm straight or ≤30 cm curve RMS plus at least 30% improvement over passive off. Stress cases display accuracy and saturation without declaring acceptable tracking.

## Truth error with passive ON

Ranges below span the three seeds, using the declared evaluation windows:

| Assumed condition | RMS true working-bar error |
|---|---:|
| Level ground with asymmetric draft | 9.7–12.5 cm |
| Straight, 1° side-slope (about 1.75% grade) | 23.3–27.7 cm |
| Straight, 2° side-slope (about 3.49% grade) | 32.7–38.4 cm |
| Straight, 3° side-slope (about 5.24% grade) | 39.0–42.4 cm |
| Right 40 m radius curve, slope and draft | 13.9–15.6 cm |
| Left 40 m radius curve, slope and draft | 18.3–20.3 cm |
| Right 25 m radius curve, soft-soil patches | 38.2–41.8 cm |
| Left 25 m radius curve, soft-soil patches | 74.3–91.2 cm |
| Matched straight side-draft with ideal GNSS | 38.5–40.0 cm |

These are **illustrative model outputs, not measured field performance**. The implement is an assumed 8 m / 2,500 kg example, not the user's machine. The reduced dynamics and coefficients need identification against real measurements before predicting field accuracy.

## What the traces indicate

The controller reduces draft error, but the richer model does **not** support claiming inch-level tracking. In the standard 3° straight case, passive OFF is about 77–90 cm RMS; ON is about 39–42 cm. There is both a persistent signed offset (about 34–37 cm) and variation around it (about 20–21 cm standard deviation).

The ideal-GNSS diagnostic remains poor. Therefore delayed/noisy GNSS alone does not explain this test's error. Traces repeatedly report **Release approaching line** while the true working bar remains on one side of the line. This is consistent with prematurely unwinding a needed steady-draft offset, but does not by itself prove causality. A targeted controller ablation/replay is the next investigation before changing production logic.

On tighter curves and weak-grip scenarios, offset or traction limits also matter. The long-trailer/tight-curve case spends essentially all its evaluation time at the 1 m offset cap. The animation and raw force/limit records make these limits visible.

## Validation of the test itself

- Zero-force straight invariance, left/right symmetry and yaw damping pass.
- An independent steady-force calculation predicts 0.3769 m offset; the integrated rigid-tractor fixture produces 0.3769 m.
- The closed-loop physics-step comparison passes the predeclared 2 cm RMS-difference tolerance (10 ms versus 5 ms).
- Every paired run remains finite, completes its duration and respects configured numerical/actuation-domain bounds.
- Playback and scenario selection are exercised in desktop and 375 px mobile browser viewports; raw CSV preserves the full 10 Hz truth and measurement traces.

The next control hypothesis is to retain the steady draft compensation while braking only the temporary extra correction. It needs tests; this round adds the evidence and does not implement that controller change.
