# ClinicCare AI Gate B — Vietnamese intent model pipeline

This document records the reproducible offline model-training gate. It is an intent-classification evaluation, not a diagnostic or clinical-safety claim.

## Audit findings

- The legacy registry has 178 manually curated synthetic Vietnamese records: 118 train, 30 validation, and 30 legacy test. It had no explicit actor, language, provenance, template-family, semantic-group, or label fields in the checked-in JSON. The model therefore trained on sparse lexical examples and did not include `SpecialtyRecommendation` or `FindDoctorForSymptom`.
- The independent benchmark is 240 generated variants from 51 labeled source templates. Those 240 rows are not 240 independent semantic situations. The separate frozen blind file has 21 synthetic cases; only 6 patient cases use labels present in the old model taxonomy for the comparable blind metric. It has zero independent human annotators.
- Gate B adds 51 new, explicitly marked `synthetic_curated` records covering hard negatives, negation, relative references, no-accent text, mixed read/write requests, OOD/gibberish, symptom-to-specialty, and symptom-to-doctor language. The effective registry is 229 records: 157 train, 42 development, and 30 legacy test.
- Effective registry validation found zero normalized duplicates, zero accent-folded duplicates, zero near duplicates at Jaccard 0.92, zero template-family leakage, zero semantic-group leakage, and zero train-to-frozen-blind leakage. Each legacy record is represented as one audited group; the generated Phase 4 dataset remains separate and is not treated as training data.

## Candidate protocol

All candidates use seed `42042`, deterministic input ordering, and do not read the frozen blind set during selection. Threshold and margin are selected only on the development split using macro-F1 with critical-intent recall, selective accuracy, and abstention as tie-break signals. A prediction is deferred to `UnclearOrOutOfScope` when `top1Score < threshold` or `top1Score - top2Score < margin`.

| Candidate | Configuration | Development Macro-F1 | Independent 108-case Macro-F1 | Independent accuracy | Frozen blind comparable |
|---|---|---:|---:|---:|---:|
| Baseline | existing SDCA artifact | 0.718 | 0.620 | 69/108 | 4/6 |
| `candidate_sdca_word` | SDCA, default word features; threshold 0.30, margin 0.00 | 0.837 | 0.580 | 67/108 | 5/6 |
| `candidate_sdca_word_char` | SDCA, word 1–2 grams + char 3–5 grams; threshold 0.15, margin 0.05 | 0.802 | 0.538 | 61/108 | 5/6 |
| `candidate_lbfgs_word_char` | L-BFGS, word + character features | 0.010 | 0.017 | 12/108 | 1/6 |

The Gate B Macro-F1 values are supported-label macro-F1: zero-support labels are excluded and listed in the machine-readable report. This is why the comparable 108-case figure is not numerically identical to the legacy Phase 4 all-taxonomy report (`69/108`, `49.63%` macro-F1).

`candidate_sdca_word` is the development winner, but its independent Macro-F1 and accuracy regress against the existing artifact. A separate repeated local run also observed L-BFGS non-determinism; the pipeline therefore keeps the deterministic-candidate requirement fail-closed. These are evidence against promotion, not grounds for tuning on blind examples.

## Promotion decision

The machine-readable decision is [`GATE_B_PROMOTION_DECISION.json`](GATE_B_PROMOTION_DECISION.json): `NOT_PROMOTED`. Independent Macro-F1 non-regression failed; the report also intentionally records `fullRegression=false` and `explicitPromotionRequested=false` so this offline command/CI cannot auto-promote. The subsequent local full backend rerun passed 450/450, but that does not override the independent-metric failure or create a promotion request. The production artifact hash and runtime mode were left unchanged; internal ML remains Shadow/Off.

`GATE_B_MODEL_REPORT.json`: Not checked in. Generate locally with `dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj --no-build --configuration Release -- --gateb --report ./gateb-model-report.json --promotion ./gateb-promotion-decision.json`; CI generates a temporary copy for validation only. The report includes per-class metrics, excluded zero-support labels, confusion matrices, score semantics, artifact hashes, threshold/margin, coverage, selective accuracy, calibration, and source HEAD/dirty state.

Gemini was not called. No API key, patient data, production database, migration, or OneDrive checkout was used.
