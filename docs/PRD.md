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

Configured via Administration → Database Configuration. All access is read-only.

## Information Architecture (v1)

```
Dashboard
Clinical
  └── Physician Statistics
Administration
  ├── Users
  ├── Roles
  ├── Database Configuration
  └── System Health
```

Future modules (Operations, Quality, Research, Finance) must plug in without navigation framework changes.

## Clinical Module — Physician Statistics

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

## Out of Scope (v1)

QA dashboards, financial reporting, research module, sonographer statistics, machine utilisation, turnaround time, HL7 monitoring, billing reconciliation, executive dashboards, scheduled reports, email notifications, Active Directory authentication.
