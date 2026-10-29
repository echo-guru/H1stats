# H1Stats — Product Requirements Document

**Version 1.0**

See [ENGINEERING_PRINCIPLES.md](./ENGINEERING_PRINCIPLES.md) for architectural expectations.

## Executive Summary

H1Stats is an internal operational intelligence platform for Hearts 1st. It transforms operational and clinical data from Siemens syngo Dynamics and other internal systems into actionable information for clinical operations, quality improvement, administration, research, and business decision making.

Version 1 establishes a robust, scalable platform architecture supporting future reporting modules without major redesign.

### Version 1 scope

- User authentication
- Role-based security
- Modular application framework
- Dashboard
- Physician Statistics module (Clinical)
- Database configuration
- User administration

## Product Vision

H1Stats is the operational intelligence platform used by Hearts 1st staff every day — not another reporting application. Users open H1Stats to understand what happened yesterday, what is happening today, where attention is required, and how the organisation is performing.

## Product Philosophy

- **Information before reports** — dashboards provide understanding; reports provide detail.
- **Modular by design** — Clinical (v1), Operations, Quality, Research, Finance, Administration as pluggable modules.
- **Widget-based interface** — reusable components (Studies Today, Pending Reports, Physician Activity, etc.).
- **Read-only** — never modifies clinical information.
- **Fast** — target response time &lt; 3 seconds.

## Technology

| Layer | Technology |
|-------|------------|
| Platform | Internal web application |
| Host | Windows Server 2024 |
| Backend | ASP.NET Core |
| Frontend | React + TypeScript |
| Database | SQL Server (read-only) |
| Auth | Application authentication (future: Active Directory) |

## Database Connections

| Setting | Value |
|---------|-------|
| Server | H1PACS (192.168.12.205) |
| Login | h1stats (dedicated SQL login) |
| Databases | mvf, AcusonDB |
| Oracle CM2 | Hearts1st (`HEARTS1ST` schema) — read-only |

Configured via Administration → Database Configuration. All access is read-only.

Oracle/CM2 semantic model: [CM2_SEMANTIC_MODEL.md](./CM2_SEMANTIC_MODEL.md).

## Information Architecture (v1)

```
Dashboard
Clinical
  ├── Reporting Dr - Syngo (Physician Statistics)
  ├── Reporting Dr - CM2
  ├── Dr Summary - CM2
  ├── Top Referring Doctors - CM2
  ├── Top Referring Practices - CM2
  ├── Referrers Growth by Quarter - CM2
  ├── Referrers Decline by Quarter - CM2
  └── Referrer 12-Month History - CM2
Administration
  ├── Users
  ├── Roles
  ├── Database Configuration
  └── System Health
```

Future modules (Operations, Quality, Research, Finance) must plug in without navigation framework changes.

## Clinical Module — Physician Statistics (Syngo)

### Filters

- Date From / Date To
- Diagnosing Physician (default: All Doctors; from `mvf.dbo.DOSR_STUDY.PHYSICIAN_READING_STUDY`)

### Database Mapping

| Field | Source |
|-------|--------|
| Study Date | `mvf.DOSR_STUDY.STUDY_DATE` |
| Study Type | `mvf.DOSR_STUDY.STUDY_DESCRIPTION` |
| Diagnosing Physician | `mvf.DOSR_STUDY.PHYSICIAN_READING_STUDY` |
| Ward | `AcusonDB.Study.Custom1` (label from `Department.CustomLabel1` = "Ward") |

### Business Rules — Inpatient vs Outpatient

**Outpatient:** Ward is NULL, or (case-insensitive) Cleveland, Toowoomba, CLEV, TWBA.

**Inpatient:** Everything else.

**Validation:** Total Studies = Outpatient + Inpatient (always).

### Report Output

Grouped by Physician → Study Type. Columns: Study Type, Total, Outpatient, Inpatient. Physician subtotals and grand totals required.

### Export

Excel, CSV. PDF planned.

## Clinical Module — Reporting Dr (CM2)

### Filters

- Date From / Date To
- Reporting Cardiologist (default: All; from `TEST.CARDIOLOGIST1` via curated lookup)

### Database Mapping

| Field | Source |
|-------|--------|
| Study Date | `HEARTS1ST.TEST.TEST_DATE` |
| Investigation Type | `TEST.TEST_REQUIRED` → semantic lookup (codes 1–12) |
| Reporting Cardiologist | `TEST.CARDIOLOGIST1` → curated name map |
| Ward | `TEST.WARD` numeric code — `1` = Outpatient; `2`–`41` = Inpatient; else Unknown (in Total only). See [CM2_SEMANTIC_MODEL.md](./CM2_SEMANTIC_MODEL.md). |

### Report Output

Grouped by Reporting Cardiologist → Investigation Type. Columns: Investigation Type, Total, Outpatient, Inpatient.

**Scope (current):** Imaging investigations only — `TEST_REQUIRED` in (1, 2, 3, 4): Transthoracic Echo, Exercise Stress Echo, Dobutamine Stress Echo, Transoesophageal Echo. ECG / Holter / event / BP are excluded.

## Clinical Module — Dr Summary (CM2)

Monthly matrix of reporting activity. Columns are cardiologist initials (JC, SYG, VM, JM, RDP, JW; extra doctors with activity are appended, excluding RHS and 1761). Rows are occasions of service.

### Filters

- Month (calendar month; default current)

### OOS mapping (study requested)

| OOS row | Source |
|---------|--------|
| Echo / Echo - InPt / Echo - Total | `TEST.TEST_REQUIRED = 1` (Transthoracic Echo). Unlabelled Echo row = outpatient (`WARD = 1`); InPt = inpatient (`WARD` 2–41). |
| DSE / DSE - InPt / DSE - Total | `TEST.TEST_REQUIRED = 3` (Dobutamine Stress Echo), same IP/OP split |
| SE / SE - InPt / SE - Total | `TEST.TEST_REQUIRED = 2` (Exercise Stress Echo), same IP/OP split |
| IOE | `TEST.TEST_REQUIRED = 4` and theatre ward (`WARD` 20 Theatre 8, 24 Theatre Admissions) |
| TEE | Remaining `TEST.TEST_REQUIRED = 4` |
| TEE - Total | All `TEST.TEST_REQUIRED = 4` (IOE + TEE) |
| Imaging subtotal | Echo-Total + DSE-Total + SE-Total + TEE-Total |
| DCCS | Diary cardioversion: `SCHEDULE_OCC_SERVICE` 6 (DCCS only) and 101 (TEE/DCCS). Cardiologist from a same-day `TEST` for that patient when present. |
| Consults | `CONSULT` joined to `LETTERS` on `LETTERS_RID`; date = `LETTER_DATE`; doctor = `CONSULT.USER_LIST_RID`. All consult types combined (initial, follow-up, and derivatives). |

ECG is excluded. Study date for imaging: `TEST.TEST_DATE`. Reporting cardiologist: `TEST.CARDIOLOGIST1`. Dr Roland Hilling-Smith (RHS) and cardiologist 1761 are omitted from this matrix.

Click a cardiologist to open the same OOS rows for that doctor across the prior 12 months (relative to the selected month).

## Clinical Module — Top Referring Doctors (CM2)

### Filters

- Date From / Date To
- Site (default: **All sites**; filters completed studies via `TEST.SITE`, with Cleveland/Toowoomba reversed vs `SCHEDULE_SITE`: Greenslopes, Cleveland, Toowoomba)
- Investigation types (default: **all tests and consults**; multi-select of `TEST.TEST_REQUIRED` plus Initial Consultation and Follow-up Consultation; Resting ECG excluded)
- Patients (default: **Inpatients and outpatients**; optional outpatients-only `WARD = 1` or inpatients-only `WARD` 2–41). Consult bookings are treated as outpatient.
- Exclude H1 Drs (optional; omits Hearts 1st cardiologists by `CM2_DOCTOR_RID`)
- Top N (editable; default **50**; server clamp 1–500)

### Database Mapping

| Field | Source |
|-------|--------|
| Study Date | Tests: `TEST.TEST_DATE`. Consults: `SCHEDULE_APPOINTMENT.APPOINTMENT_DATE`. |
| Investigation Type | `TEST.TEST_REQUIRED` → semantic lookup; consults = arrived `SCHEDULE_APPOINTMENT` in `SCHEDULE_OCC_SERVICE.SCHEDULE_GROUP_RID = 5`. Initial vs follow-up from `OCC_SERVICE_TITLE` (`INITIAL` → Initial Consultation; otherwise Follow-up Consultation). |
| Referring Doctor (person) | Tests: `TEST.REFERRING_DOCTOR` → `DOCTOR` → `CM2_DOCTOR`. Consults: `PATIENT.PAT_REFERRING_DOCTOR` → `DOCTOR` → `CM2_DOCTOR`. |
| Site | UI campus is `SCHEDULE_SITE` (1 Greenslopes, 2 Cleveland, 3 Toowoomba). Completed studies filter `TEST.SITE` with Cleveland/Toowoomba reversed (`TEST.SITE` 3 / 2). NULL = unassigned (included only when Site = All). Consults filter `SCHEDULE_SITE` via appointment room (no Cleveland/Toowoomba swap). |
| Ward | `TEST.WARD` — `1` = Outpatient; `2`–`41` = Inpatient; else Unknown |

### Reporting Grain

**Doctor person** — consolidate locations under `CM2_DOCTOR_RID` when linked; otherwise one unresolved person per legacy `DOCTOR_RID`.

### Report Output

Ranked by total studies (DESC), top N. Grouped by Referring Doctor → Investigation Type. Columns: Investigation Type, Total, Outpatient, Inpatient. Doctor subtotals and grand totals.

### Export

CSV.

## Clinical Module — Top Referring Practices (CM2)

### Filters

- Date From / Date To
- Site (default: **All sites**; filters `TEST.SITE` with Cleveland/Toowoomba reversed vs `SCHEDULE_SITE`)
- Top N (editable; default **50**; server clamp 1–500)

### Hierarchy

1. **Practice** (`CM2_PRACTICE` via `DOCTOR.CM2_PRACTICE_RID`) — ranked by total referrals
2. **Referring doctor / provider** within practice — by normalised Medicare provider number (fallback: legacy `DOCTOR_RID`), sorted by total descending
3. **Investigation type** for that provider — Total / Outpatient / Inpatient

Resting ECG (`TEST_REQUIRED = 5`) is excluded, consistent with Top Referring Doctors.

### Export

CSV.

## Clinical Module — Referrers Growth by Quarter (CM2)

### Filters

- Year / Quarter (default: last completed calendar quarter)
- Site (default: All sites; filters `TEST.SITE` with Cleveland/Toowoomba reversed vs `SCHEDULE_SITE`)
- Investigation Type (default: All tests; Resting ECG excluded)
- Top N (editable; default **50**)

### Logic

1. Take referring doctors (person grain) with activity in the **selected quarter**
2. Compare totals to the **previous quarter**
3. Rank by absolute growth (`current − previous`) descending; show growth % (or “New” when previous = 0)

### Export

CSV.

## Clinical Module — Referrers Decline by Quarter (CM2)

Same filters and expandable investigation-type breakdown as Referrers Growth.

### Logic

1. Take referring doctors with activity in the **previous quarter**
2. Keep only those with a **negative** change in the selected quarter (includes drops to zero)
3. Rank by largest absolute drop or largest % drop

### Export

CSV.

## Clinical Module — Referrer 12-Month History (CM2)

### Filters

- Search by **Doctor** or **Practice** (required; typeahead)
- Investigation Type (default: **All tests**; Resting ECG excluded)

Date range is fixed: the current calendar month plus the previous 11 months.

### Logic

1. **Doctor** — person grain (`CM2_DOCTOR_RID`, else unresolved legacy `DOCTOR_RID`). Totals combine every practice location that person referred from, and all Hearts 1st sites.
2. **Practice** — `CM2_PRACTICE_RID`.
3. Show 12 month rows (zero-filled) with Total / Outpatient / Inpatient. Expand a month for investigation-type breakdown.

### Export

CSV.

## Out of Scope (v1)

QA dashboards, financial reporting, research module, sonographer statistics, machine utilisation, turnaround time, HL7 monitoring, billing reconciliation, executive dashboards, scheduled reports, email notifications, Active Directory authentication.
