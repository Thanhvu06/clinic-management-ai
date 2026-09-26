# Phase 2 conversation and entity model

## Intent hierarchy

Existing canonical intents remain compatible. Phase 2 adds routing metadata for
`SpecialtyRecommendation`, `FindDoctorForSymptom`, `EmergencyEscalation`, and
`PromptInjection`. The classifier distinguishes greeting, symptom/reason,
specialty recommendation, doctor lookup, doctor/slot selection, booking draft
review/confirmation/modification/cancellation, appointment lookup, pricing,
facility FAQ, safety and out-of-scope input.

## Typed entities

`AiExtractedEntity` carries type, normalized value, source, confidence,
validation state and rejection reason. Current first-pass entities include
`ClinicalReason`, `DoctorName`, and `NegatedDoctor`; server snapshots remain the
source for relative doctor/slot indexes. IDs supplied by a client or model are
revalidated by the domain service.

## Reason extraction rules

- `có bác sĩ nào khám bệnh ho không` → specialty recommendation, reason `Ho`,
  no doctor name.
- `tôi bị đau bụng thì nên chọn bác sĩ nào\` → specialty recommendation,
  reason `Đau bụng`, no trailing escape and no doctor name.
- `cho tôi xem bác sĩ Nguyễn Minh Khải` → explicit doctor lookup.
- command-only, relative-selection-only and repeated-gibberish input cannot be
  persisted as a clinical reason.
- new clinical clauses are merged with an existing reason rather than
  overwriting it.

Normalization is repeated on the backend even when the frontend already
normalized display text.
