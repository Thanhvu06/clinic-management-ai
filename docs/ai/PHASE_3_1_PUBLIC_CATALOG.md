# ClinicCare AI Phase 3.1 — Public catalog

Phase 3.1 extends the existing read-only `clinic.search_knowledge` tool. It
does not create a second chatbot or a write-capable tool, and it is not the
completion of Phase 3.

## Contract

The tool is versioned as `1.0` and accepts one closed JSON object:

```json
{
  "entity": "all|specialty|doctor|diagnostic_service|facility|price",
  "query": "public search text",
  "specialtyQuery": "optional public filter",
  "facilityQuery": "optional public filter",
  "limit": 10
}
```

`query` is required and is limited to 160 characters. The optional filters are
limited to 120 characters and `limit` is restricted to 1–20. Unknown fields,
objects/arrays, invalid entities, and client-supplied authority fields such as
`userId`, `role`, `facilityId`, and `facilityAuthorization` are rejected before
query execution. Actor, role and facility permissions remain server-owned.

## Approved data sources and projection

The handler uses bounded, stable database candidate queries (maximum 200
candidates per source) and returns at most the requested result limit. Matching
normalizes Vietnamese accents and compares meaningful terms without loading an
unbounded table into memory.

| `entity` | Source and public projection |
| --- | --- |
| `specialty` | Active `Specialty`: code, name, description and consultation fee only when published and positive. |
| `doctor` | Active `Doctor` plus active user, active specialty and an active `Doctor` facility assignment. Public title, experience, description, specialty names and assigned public facility names/address/city are returned. |
| `diagnostic_service` | Active `DiagnosticService`: code, name, category, preparation instructions and published positive price when present. |
| `facility` | Active `Facility` and active `ClinicLocation`: code, name, public address/city/phone/description and stored opening-hours text. |
| `price` | Positive current consultation, diagnostic-service and health-package prices, each paired with its own source entity and price type. |
| `all` | The bounded union of the approved public projections above. |

Patient records, medical records, national identifiers, internal account
fields, private doctor contact data, private schedules, inventory and
administrative data are not projected. A doctor is excluded if the doctor,
user, specialty, assignment or facility is inactive. No inactive diagnostic
service or unpublished/non-positive price is returned.

The response is a `clinic_knowledge` result with `status` `matched` or
`not_found`, `sourceType`, public `items`, `retrievedAtUtc`, and the verified
`clinic_public_catalog`/`approved_database` source metadata. `retrievedAtUtc`
is the read time; it is not presented as a source update timestamp. A missing
match is returned as `not_found` and never falls back to arbitrary top records.

## Routing and grounding

The deterministic planner routes catalog questions such as:

- “Có khám chuyên khoa tim mạch không?” → `entity=specialty`.
- “Bác sĩ nào khám chuyên khoa nội tổng quát ở cơ sở trung tâm?” → `entity=doctor`.
- “Dịch vụ siêu âm bụng giá bao nhiêu?” → `entity=price`.
- “Cơ sở X ở đâu?” or “Chủ nhật cơ sở có mở không?” → `entity=facility`.

Gemini and ML.NET may provide routing hints elsewhere in the AI pipeline, but
they are not sources of catalog truth or authorization. Deterministic catalog
lookup remains available when a provider is not called or is unavailable. For
opening hours, the response can only expose stored `OpeningHours`; it must not
infer Sunday availability from weekday hours.

## Verification boundary

Integration tests exercise the copilot API and a temporary seeded test
database for accent/no-accent matching, live price changes, inactive records,
no-random-fallback behavior, exact price pairing, public-field projection,
schema/authority rejection, planner routing and stored-hours behavior. They do
not prove generalization to unseen language, live Gemini behavior, production
SQL Server performance, or the public-policy correctness of fields beyond the
projection listed above. No EF model was changed for this phase, so no
migration is required and no real database migration is applied.
