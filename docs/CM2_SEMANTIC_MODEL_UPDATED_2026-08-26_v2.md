# Hearts1st CM2 Semantic Data Model

Authoritative reference for H1Stats Oracle/CM2 queries, joins, and naming.
Source: CM2 DB master mapping (Hearts1st).

## Purpose

The Hearts1st Oracle database contains 100+ physical tables. H1Stats uses a smaller semantic model of clinically meaningful entities rather than inferring all physical relationships.

Core entities: Patient, Study, Investigation Type, Report, Doctor, Consultation, Letter, Document, modality-specific data.

---

## Core Principles

1. **`HEARTS1ST.TEST` is the authoritative study table.** One row = one investigation. Child tables supplement; they do not determine study existence.
2. **`TEST.TEST_REQUIRED` defines investigation type.** Map codes to semantic names in the application layer.
3. **Count studies with `COUNT(DISTINCT TEST.TEST_RID)`** after joins that may be one-to-many.
4. **Date filter (half-open):** `TEST.TEST_DATE >= :DateFrom AND TEST.TEST_DATE < :DateToExclusive`.
5. **Preserve unmatched studies** via left joins for optional relationships.
6. **Unknown codes:** label as `Unknown – Code <value>`; do not exclude silently.
7. **Doctor roles must stay separate** (reporting vs referring vs performing).
8. **Correspondence is a separate domain from studies.** `CONSULT`, `LETTERS`, and `DOCUMENTS` must not be treated as `TEST` modality children.
9. **Preserve source codes even when multiple codes are grouped semantically.**

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

---

## Consultation, Letters and Documents

These tables represent clinical correspondence and document storage outside the core `TEST` study hierarchy. They are **not modality child tables of `TEST`**.

### Table grains and row counts

| Table | Grain | Primary key | Rows observed 26 Aug 2026 | Purpose |
|------|-------|-------------|---------------------------:|---------|
| `CONSULT` | One consultation-classified record | `CONSULT_RID` | 91,505 | Structured consultation/report classification linked to a `LETTERS` record |
| `LETTERS` | One generated letter/correspondence record | `LETTERS_RID` | 113,752 | CM2-generated clinical correspondence with date, title and file path |
| `DOCUMENTS` | One stored/imported document | `DOCUMENTS_RID` | 874,313 | Broader patient document repository including PDFs, referrals, results, ECG files and other imported material |

### `CONSULT` structure

| Column | Type | Null? | Semantic use |
|--------|------|-------|--------------|
| `CONSULT_RID` | `NUMBER(10)` | No | Primary key |
| `LETTERS_RID` | `NUMBER(10)` | No | Logical link to `LETTERS.LETTERS_RID` |
| `CONSULT_STATUS` | `NUMBER(3)` | Yes | Consultation status code; meanings not yet mapped |
| `CONSULT_TYPE` | `NUMBER(3)` | Yes | Consultation semantic type |
| `PATIENT_RID` | `NUMBER(10)` | No | Patient identifier |
| `USER_LIST_RID` | `NUMBER(4)` | Yes | Reporting cardiologist/user ID; same ID space currently used for `TEST.CARDIOLOGIST1` |

`CONSULT` has no `TEST_RID`; do not join it as a study child.

### `CONSULT` → `LETTERS` relationship

Logical join:

```text
CONSULT.LETTERS_RID → LETTERS.LETTERS_RID
```

This relationship is not enforced as an Oracle foreign key, but was validated empirically on 26 Aug 2026:

- Total `CONSULT` rows: **91,505**
- Matching `LETTERS` rows: **91,487**
- Missing letter rows: **18**
- Patient mismatches where both rows exist: **0**
- Match rate: **99.98%**

Use a **left join** from `CONSULT` to `LETTERS` so the 18 unmatched consultations are preserved. Where a letter exists, `CONSULT.PATIENT_RID` and `LETTERS.PATIENT_RID` have been observed to agree.

Consult reporting date is:

```text
CONSULT.LETTERS_RID → LETTERS.LETTERS_RID → LETTERS.LETTER_DATE
```

### Consultation type mapping

The consultation type is strongly coupled to `LETTERS.LETTER_TYPE`:

| `CONSULT_TYPE` | `LETTER_TYPE` | Count observed | Semantic name | Notes |
|---------------:|--------------:|---------------:|---------------|-------|
| 1 | 6 | 17,937 | **Initial Consultation** | Letter title convention `110` |
| 2 | 7 | 72,356 | **Follow-up Consultation** | Letter title convention `116` |
| 3 | 11 | 359 | **Internal Report – External Test** | Internal report documenting an externally performed investigation |
| 4 | 12 | 835 | **Internal Report – External Test** | Internal report documenting an externally performed investigation |

Four type-1 and fourteen type-2 consultations had no matching `LETTERS` row. `CONSULT_TYPE` 3 and 4 are grouped semantically for now, but the original source code must be retained because a meaningful distinction may later be identified.

Suggested application mapping:

```sql
CASE c.CONSULT_TYPE
    WHEN 1 THEN 'Initial Consultation'
    WHEN 2 THEN 'Follow-up Consultation'
    WHEN 3 THEN 'Internal Report – External Test'
    WHEN 4 THEN 'Internal Report – External Test'
    ELSE 'Unknown – Code ' || TO_CHAR(c.CONSULT_TYPE)
END AS CONSULT_TYPE_NAME
```

### Consultation status

Observed `CONSULT_STATUS` distribution:

| Code | Count |
|-----:|------:|
| 5 | 850 |
| 6 | 28 |
| 7 | 32 |
| 8 | 5 |
| 9 | 90,590 |

The semantic meanings of status codes 5–9 are **not yet established**. Preserve the raw code and do not infer labels.

### `LETTERS` structure

| Column | Type | Null? | Semantic use |
|--------|------|-------|--------------|
| `LETTERS_RID` | `NUMBER(10)` | No | Primary key |
| `PATIENT_RID` | `NUMBER(10)` | No | Patient identifier; Oracle declares this as a foreign key |
| `LETTER_TITLE` | `VARCHAR2(50)` | Yes | CM2 display/title field |
| `LETTER_PATH` | `VARCHAR2(100)` | Yes | Stored file path |
| `LETTER_DATE` | `DATE` | Yes | Letter/report date; authoritative date for consult correspondence |
| `LETTER_TYPE` | `NUMBER(2)` | Yes | Letter category code |
| `USER_LIST_RID` | `NUMBER(10)` | Yes | User/reporting identity field; exact FK semantics not yet fully mapped |

Sample consultation files use paths such as:

```text
\letters\2003\2201.rtf
```

`LETTER_PATH`, not `LETTER_TITLE`, is the field to use when the physical filename/path is required.

### `LETTER_TYPE` distribution

| `LETTER_TYPE` | Count | Current interpretation | Confidence |
|--------------:|------:|------------------------|------------|
| 1 | 1,164 | Older doctor correspondence | Provisional; title pattern only |
| 2 | 18,513 | Doctor/clinical correspondence | Provisional; title pattern only |
| 3 | 1,706 | Patient correspondence | Provisional; title pattern only |
| 6 | 17,939 | **Initial Consultation** | Confirmed (`110`) |
| 7 | 72,358 | **Follow-up Consultation** | Confirmed (`116`) |
| 9 | 878 | Discharge summary | Strongly indicated by titles; retain code |
| 11 | 359 | **Internal Report – External Test** | Confirmed grouping for current use |
| 12 | 835 | **Internal Report – External Test** | Confirmed grouping for current use |

`LETTERS` is therefore broader than consultation correspondence. Do **not** equate all `LETTERS` rows with consultations. `CONSULT` identifies the consultation-classified subset.

### Most recent consultation letter per patient

For clinical retrieval, restrict to `LETTER_TYPE IN (6,7)` and select the latest `LETTER_DATE`; use `LETTERS_RID` as a deterministic tie-breaker:

```sql
ROW_NUMBER() OVER (
    PARTITION BY l.PATIENT_RID
    ORDER BY l.LETTER_DATE DESC, l.LETTERS_RID DESC
)
```

This provides the latest initial/follow-up consultation letter independently of the `CONSULT` table, and returns the physical path via `LETTER_PATH`.

### `DOCUMENTS` structure

| Column | Type | Null? | Semantic use |
|--------|------|-------|--------------|
| `DOCUMENTS_RID` | `NUMBER(10)` | No | Primary key |
| `PATIENT_RID` | `NUMBER(10)` | No | Patient identifier |
| `DOC_TITLE` | `VARCHAR2(50)` | Yes | Human-readable document title |
| `DOC_PATH` | `VARCHAR2(100)` | Yes | Stored file path |
| `DOC_DATE` | `DATE` | Yes | Document date |
| `DOC_CATEGORY` | `NUMBER(2)` | Yes | Document category code; mapping unresolved |
| `USER_LIST_RID` | `NUMBER(10)` | Yes | User identity field; semantics unresolved |
| `DOC_INBOX` | `NUMBER(1)` | Yes | Inbox/status flag; semantics unresolved |

Observed examples show `DOCUMENTS` is a heterogeneous repository, including:

- echo documents/PDFs
- referral letters
- consultation questionnaires
- blood results
- ECG files (including `.SCP`)

Example paths include:

```text
\documents\2003\808.pdf
\documents\2003\826.SCP
```

Do not treat `DOCUMENTS` as equivalent to `LETTERS`. `LETTERS` is the cleaner structured source for CM2-generated correspondence; `DOCUMENTS` is a much broader imported/stored-document corpus.

### Current correspondence/document model

```text
PATIENT
  │
  ├── TEST ──> REPORTS / modality child tables
  │
  ├── CONSULT
  │      │
  │      └── LETTERS     (consultation/report correspondence)
  │
  ├── LETTERS            (broader CM2-generated correspondence)
  │
  └── DOCUMENTS          (broader imported/stored clinical documents)
```

For a patient-level clinical summary, these are separate source domains:

- **Latest consultation letter:** `LETTERS`, `LETTER_TYPE IN (6,7)`
- **Latest TTE:** `TEST`, `TEST_REQUIRED = 1`, then `REPORTS` via `TEST_RID`
- **Latest exercise stress echo:** `TEST`, `TEST_REQUIRED = 2`, then `REPORTS` via `TEST_RID`

Do not assume a physical report filename/path for TTE or stress echo until the relevant path fields in `TTX_ECHO`, `STRESS_ECHO`, `REPORTS`, or another linked table have been explicitly mapped.

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

## Site (`TEST.SITE` → `SCHEDULE_SITE`)

Campus for a **completed study** is stored on `TEST.SITE`, which is `SCHEDULE_SITE.SCHEDULE_SITE_RID`.

| `SCHEDULE_SITE_RID` | Site name | Common aliases |
|---------------------|-----------|----------------|
| 1 | Greenslopes | GSL |
| 2 | Cleveland | CLEV |
| 3 | Toowoomba | TWBA |
| NULL | Unassigned (older studies) | — |

```
TEST.SITE = SCHEDULE_SITE.SCHEDULE_SITE_RID
```

Do **not** infer campus from `SCHEDULE_GROUP` or `SCHEDULE_OCC_SERVICE`. Those are the booking diary:

```
SCHEDULE_OCC_SERVICE.SCHEDULE_GROUP_RID → SCHEDULE_GROUP   (service family: Echo, Stress, Monitoring, …)
SCHEDULE_ROOM.SCHEDULE_SITE_RID        → SCHEDULE_SITE    (diary rooms at a campus)
SCHEDULE_NOTE.SCHEDULE_SITE_RID        → SCHEDULE_SITE    (diary notes at a campus)
```

`SCHEDULE_GROUP` is a service grouping (Echo / TEE / DCCS, Stress, Monitoring Attach, …), not Greenslopes / Cleveland / Toowoomba.

When a referring-doctor report is filtered to one site, rank and counts include only studies with that `TEST.SITE`. Unassigned (`NULL`) studies appear only when Site = All.

---


## Patient Contact Numbers

CM2 stores four patient contact-number fields plus a separate preferred-number selector.

| Semantic field | Source column |
|---|---|
| Mobile | `PATIENT.PAT_MOB_PHONE` |
| Home | `PATIENT.PAT_HOME_PHONE` |
| Work | `PATIENT.PAT_WORK_PHONE` |
| Other | `PATIENT.PAT_CONTACT_NUM` |

`PATIENT.PAT_CONTACT_NAME` stores the name associated with the Other/contact number.

`PATIENT.PAT_PREFERRED_PHONE_NUM` is a numeric selector that determines which stored number CM2 treats/displays as the preferred contact number.

| `PAT_PREFERRED_PHONE_NUM` | Preferred number | Source field |
|---:|---|---|
| 1 | Mobile | `PAT_MOB_PHONE` |
| 2 | Home | `PAT_HOME_PHONE` |
| 3 | Work | `PAT_WORK_PHONE` |
| 4 | Other | `PAT_CONTACT_NUM` |

Conceptual derivation:

```sql
CASE p.PAT_PREFERRED_PHONE_NUM
    WHEN 1 THEN p.PAT_MOB_PHONE
    WHEN 2 THEN p.PAT_HOME_PHONE
    WHEN 3 THEN p.PAT_WORK_PHONE
    WHEN 4 THEN p.PAT_CONTACT_NUM
END AS PREFERRED_PHONE
```

Important: the Other number remains stored in `PAT_CONTACT_NUM` whether or not it is selected as preferred. `PAT_PREFERRED_PHONE_NUM` only controls which of the four stored numbers is preferred/displayed.

## Development Rules

1. Use `TEST` as the source of study activity.
2. Use `TEST.TEST_REQUIRED` as the authoritative investigation type.
3. Count with `COUNT(DISTINCT TEST.TEST_RID)`.
4. Keep referring-doctor and reporting-cardiologist dimensions separate.
5. Do not merge ambiguous doctors without review.
6. Use `TEST.SITE` (not `SCHEDULE_GROUP` / `SCHEDULE_OCC_SERVICE`) for campus on completed studies.
7. Add newly confirmed relationships here before using them in production reports.

## Unresolved (do not invent)

- Full identity-resolution admin (provider-key merge, manual mapping UI)
- Performing / supervising / ordering roles
- `CALCULATIONS` / `CALCULATIONS_REPORT` detail
- Semantic meanings of `CONSULT_STATUS` codes 5–9
- Exact distinction between `CONSULT_TYPE` 3 and 4 / `LETTER_TYPE` 11 and 12
- Authoritative semantic names for `LETTER_TYPE` 1, 2 and 3
- `DOCUMENTS.DOC_CATEGORY` mapping
- `DOCUMENTS.DOC_INBOX` meaning
- Exact lookup/FK targets and semantics for `LETTERS.USER_LIST_RID` and `DOCUMENTS.USER_LIST_RID`
- Physical report filename/path mapping for TTE and stress echo
