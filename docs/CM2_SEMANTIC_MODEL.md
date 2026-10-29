# Hearts1st CM2 Semantic Data Model

Authoritative reference for H1Stats Oracle/CM2 queries, joins, and naming.
Source: CM2 DB master mapping (Hearts1st).

## Purpose

The Hearts1st Oracle database contains 100+ physical tables. H1Stats uses a smaller semantic model of clinically meaningful entities rather than inferring all physical relationships.

Core entities: Patient, Study, Investigation Type, Report, Doctor, modality-specific data.

---

## Core Principles

1. **`HEARTS1ST.TEST` is the authoritative study table.** One row = one investigation. Child tables supplement; they do not determine study existence.
2. **`TEST.TEST_REQUIRED` defines investigation type.** Map codes to semantic names in the application layer.
3. **Count studies with `COUNT(DISTINCT TEST.TEST_RID)`** after joins that may be one-to-many.
4. **Date filter (half-open):** `TEST.TEST_DATE >= :DateFrom AND TEST.TEST_DATE < :DateToExclusive`.
5. **Preserve unmatched studies** via left joins for optional relationships.
6. **Unknown codes:** label as `Unknown – Code <value>`; do not exclude silently.
7. **Doctor roles must stay separate** (reporting vs referring vs performing).

---

## Investigation Type (`TEST.TEST_REQUIRED`)

| Code | Semantic name | Dedicated child table |
|------|---------------|-----------------------|
| 1 | Transthoracic Echo | `TTX_ECHO` |
| 2 | Exercise Stress Echo | `STRESS_ECHO` |
| 3 | Dobutamine Stress Echo | `DOB_ECHO` |
| 4 | Transoesophageal Echo | `TEE_ECHO` |
| 5 | Resting ECG | None |
| 6 | Exercise ECG | `EX_ECG` |
| 7 | Holter Monitor | `HOLTER` |
| 8 | Seven-Day Event Monitor | None |
| 9 | Blood Pressure Monitor | `BP` |
| 10 | Coronary Angiography | `ANGIO` |
| 11 | CT Coronary Angiography | `CTCA` |
| 12 | Cardiac MRI | `CMR` |

Suggested categories: Echocardiography (1–4), Electrocardiography (5–6), Ambulatory Monitoring (7–9), Invasive Cardiology (10), Cardiac Imaging (11–12).

Do not use `SCHEDULE_OCC_SERVICE` to classify completed studies.

---

## Study → Patient / Report / Modality

```
PATIENT.PATIENT_RID ← TEST.PATIENT_RID
REPORTS.TEST_RID = TEST.TEST_RID          (optional; left join)
<MODALITY>.TEST_RID = TEST.TEST_RID      (optional; left join)
```

- Resting ECG (`TEST_REQUIRED = 5`): no `REPORTS`, no modality child — count from `TEST`.
- Seven-Day Event Monitor (`= 8`): has `REPORTS`, no modality child — count from `TEST`.
- `CONSULT` has no `TEST_RID` — do not join as a study child.
- Consult reporting date is `LETTERS.LETTER_DATE` via `CONSULT.LETTERS_RID`.
- Consult reporting cardiologist is `CONSULT.USER_LIST_RID` (same ID space as `TEST.CARDIOLOGIST1`).
- Intra-operative TEE (IOE) is `TEST_REQUIRED = 4` with theatre ward (`WARD` 20 or 24).

### Diary TEE / IOE bookings

Past TEE/IOE progress uses the diary, not `TEST`, as the starting list:

| `SCHEDULE_OCC_SERVICE_RID` | Title | Kind |
|----------------------------|-------|------|
| 3 | Transoesophageal Echo | TEE |
| 4 | Inpatient Transoesophageal Echo | TEE |
| 5 | Intra-operative Transoesophageal Echo | IOE |

Do not include cardioversion (6 / 101). Match a Syngo `DOSR_STUDY` (description TEE / IOE) and a `TEST` report (`TEST_REQUIRED = 4`, `REPORT_PATH` present) on the same patient within 7 days of the appointment.

The reverse gap is also listed: a Syngo TEE/IOE study in the date range with no matching CM2 diary booking and no matching `TEST` (`TEST_REQUIRED = 4`).

---

## Reporting Cardiologist

Source: `TEST.CARDIOLOGIST1` — **not** a `DOCTOR_RID`. Curated lookup only.

| Cardiologist ID | Display name |
|-----------------|--------------|
| 1 | Undefined Dr |
| 1681 | Dr Vance Manins |
| 282 | Dr Julie Ch'ng |
| 1941 | Dr Justin Morze |
| 2901 | Dr Shi Yi Goo |
| 144 | Dr Roess D Pascoe |
| 981 | Dr Jeremy Wright |
| 3581 | Dr Roland Hilling-Smith |

Never join `CARDIOLOGIST1` directly to `DOCTOR.DOCTOR_RID`.

`TEST.CARDIOLOGIST2` is optional/secondary and out of initial scope.

---

## Referring Doctor

```
TEST.REFERRING_DOCTOR → DOCTOR.DOCTOR_RID
  DOCTOR.CM2_DOCTOR_RID → CM2_DOCTOR.CM2_DOCTOR_RID   (person)
  DOCTOR.CM2_PRACTICE_RID → CM2_PRACTICE.CM2_PRACTICE_RID
  DOCTOR.DOCTOR_PROV_ID → provider number (location-specific)
```

- Study-level `REFERRING_DOCTOR` is authoritative for historical reporting.
- Do **not** use `PATIENT.PAT_REFERRING_DOCTOR` for study history.
- A `DOCTOR` row is a legacy source record (person + practice + provider + location), not necessarily one unique person.
- Person grain (v1 default): prefer valid `CM2_DOCTOR_RID`; else unresolved legacy `DOCTOR_RID`.
- Preserve original `DOCTOR_RID` for audit.

### Reporting grains

| Grain | Group by | Use |
|-------|----------|-----|
| Source record | `DOCTOR_RID` | Audit / reconciliation |
| Provider location | Full provider number | Location analysis |
| **Doctor person** | `CM2_DOCTOR_RID` (or legacy surrogate) | Individual referrer totals (v1) |
| Practice | `CM2_PRACTICE_RID` | Practice / geography |

### Conceptual study join

```sql
FROM HEARTS1ST.TEST t
LEFT JOIN HEARTS1ST.DOCTOR d
  ON d.DOCTOR_RID = t.REFERRING_DOCTOR
LEFT JOIN HEARTS1ST.CM2_DOCTOR cd
  ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
LEFT JOIN HEARTS1ST.CM2_PRACTICE cp
  ON cp.CM2_PRACTICE_RID = d.CM2_PRACTICE_RID
```

### Display name

Prefer CM2: `DOC_TITLE` + `DOC_FIRSTNAME` + `DOC_MIDDLENAME` + `DOC_LASTNAME`.  
Fallback: legacy `DOCTOR_TITLE` + `DOCTOR_FIRSTNAME` + `DOCTOR_LASTNAME`.

---

## Ward (`TEST.WARD`)

`TEST.WARD` is a **numeric code**, not a free-text label.

| Code | Ward name |
|------|-----------|
| 0 | Unknown |
| 1 | Outpatient |
| 2 | 5 |
| 3 | 11 |
| 4 | 13 |
| 5 | 21 |
| 6 | 23 |
| 7 | 25 |
| 8 | 31 |
| 9 | 33 |
| 10 | 35 |
| 11 | 41 |
| 12 | 43 |
| 13 | 45 |
| 14 | CCU |
| 15 | EC |
| 16 | ICU |
| 17 | FSU |
| 18 | KPU |
| 19 | Rehab |
| 20 | Theatre 8 |
| 21 | Day Surgery |
| 22 | Sunnybank Private |
| 23 | Belmont Private |
| 24 | Theatre Admissions |
| 25 | Inpatient |
| 26 | 43 |
| 27 | 37 |
| 28 | 47 |
| 29 | CCL |
| 30 | St Raphael's |
| 31 | St Damien's |
| 32 | St Luke's |
| 33 | St Gabriel's |
| 34 | St Anne's |
| 35 | ICU |
| 36 | Maternity |
| 37 | St Michael's |
| 38 | Day Surgery |
| 39 | EC |
| 40 | 12 |
| 41 | 22 |
| other | Unmapped |

### IP / OP rule

```text
WHEN ward = 1           THEN Outpatient
WHEN ward BETWEEN 2 AND 41 THEN Inpatient
ELSE Unknown
```

Unknown (null, 0, outside 1–41) is included in Total but not in Outpatient or Inpatient, so Total may be greater than Outpatient + Inpatient.

---

## Site (`SCHEDULE_SITE` vs `TEST.SITE`)

Diary campus (rooms, notes, consult bookings) is `SCHEDULE_SITE.SCHEDULE_SITE_RID`:

| `SCHEDULE_SITE_RID` | Site name | Common aliases |
|---------------------|-----------|----------------|
| 1 | Greenslopes | GSL |
| 2 | Cleveland | CLEV |
| 3 | Toowoomba | TWBA |
| NULL | Unassigned (older studies) | — |

Completed studies store campus on `TEST.SITE`. Greenslopes matches (`1` = `1`). **Cleveland and Toowoomba are reversed** on `TEST.SITE` relative to `SCHEDULE_SITE`:

| Campus | `SCHEDULE_SITE_RID` (diary / UI) | `TEST.SITE` (completed studies) |
|--------|----------------------------------|----------------------------------|
| Greenslopes | 1 | 1 |
| Cleveland | 2 | 3 |
| Toowoomba | 3 | 2 |

Confirmed against same-day diary rooms and Toowoomba hospital wards on `TEST.WARD` (St Raphael's, St Damien's, St Luke's, … sit on `TEST.SITE = 2`). Do **not** join `TEST.SITE = SCHEDULE_SITE_RID` to name Cleveland / Toowoomba.

When a referring-doctor report is filtered to one site, translate the UI/`SCHEDULE_SITE` code to `TEST.SITE` (2 ↔ 3) before filtering. Unassigned (`NULL`) studies appear only when Site = All.

Do **not** infer campus from `SCHEDULE_GROUP` or `SCHEDULE_OCC_SERVICE`. Those are the booking diary:

```
SCHEDULE_OCC_SERVICE.SCHEDULE_GROUP_RID → SCHEDULE_GROUP   (service family: Echo, Stress, Monitoring, …)
SCHEDULE_ROOM.SCHEDULE_SITE_RID        → SCHEDULE_SITE    (diary rooms at a campus)
SCHEDULE_NOTE.SCHEDULE_SITE_RID        → SCHEDULE_SITE    (diary notes at a campus)
```

`SCHEDULE_GROUP` is a service grouping (Echo / TEE / DCCS, Stress, Monitoring Attach, …), not Greenslopes / Cleveland / Toowoomba.

---

## Development Rules

1. Use `TEST` as the source of study activity.
2. Use `TEST.TEST_REQUIRED` as the authoritative investigation type.
3. Count with `COUNT(DISTINCT TEST.TEST_RID)`.
4. Keep referring-doctor and reporting-cardiologist dimensions separate.
5. Do not merge ambiguous doctors without review.
6. Use `TEST.SITE` for campus on completed studies, but map Cleveland/Toowoomba as `TEST.SITE` 3/2 (not `SCHEDULE_SITE_RID` 2/3). Use room → `SCHEDULE_SITE` for diary campus.
7. Add newly confirmed relationships here before using them in production reports.

## Unresolved (do not invent)

- Full identity-resolution admin (provider-key merge, manual mapping UI)
- Performing / supervising / ordering roles
- `CALCULATIONS` / `CALCULATIONS_REPORT` detail
