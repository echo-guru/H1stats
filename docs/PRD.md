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
  ├── Top Referring Doctors - CM2
  └── Top Referring Practices - CM2
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

## Clinical Module — Top Referring Doctors (CM2)

### Filters

- Date From / Date To
- Investigation Type (default: **All tests**; filters `TEST.TEST_REQUIRED`)
- Top N (editable; default **50**; server clamp 1–500)

### Database Mapping

| Field | Source |
|-------|--------|
| Study Date | `HEARTS1ST.TEST.TEST_DATE` |
| Investigation Type | `TEST.TEST_REQUIRED` → semantic lookup |
| Referring Doctor (person) | `TEST.REFERRING_DOCTOR` → `DOCTOR.DOCTOR_RID` → `CM2_DOCTOR` (fallback: unresolved legacy `DOCTOR_RID`) |
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
- Top N (editable; default **50**; server clamp 1–500)

### Hierarchy

1. **Practice** (`CM2_PRACTICE` via `DOCTOR.CM2_PRACTICE_RID`) — ranked by total referrals
2. **Referring doctor / provider** within practice — by normalised Medicare provider number (fallback: legacy `DOCTOR_RID`), sorted by total descending
3. **Investigation type** for that provider — Total / Outpatient / Inpatient

Resting ECG (`TEST_REQUIRED = 5`) is excluded, consistent with Top Referring Doctors.

### Export

CSV.

## Out of Scope (v1)

QA dashboards, financial reporting, research module, sonographer statistics, machine utilisation, turnaround time, HL7 monitoring, billing reconciliation, executive dashboards, scheduled reports, email notifications, Active Directory authentication.
