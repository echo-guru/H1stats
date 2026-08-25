# New Referrer Alert — Implementation Plan

**Status:** Planned — not active  
**Purpose:** Automatically identify referring doctors who had no referrals in the prior 12 months and then receive a new referral, and post a digest to a Slack channel so cardiologists know who deserves a specialized phone call.  
**Polling cadence (proposed):** Every hour  

---

## Business goal

When a referring doctor refers a patient after a long gap (no activity in the last 12 months), the clinical team should be notified quickly so a cardiologist can make a relationship-building phone call.

This is **outreach intelligence**, not a clinical alert. The Slack message is a work queue for the team, not a patient-facing notification.

---

## Detection rule

### Definition

A **new referrer candidate** is a referring doctor who:

1. Has at least one referral (`TEST` row) in the **poll window** (since last successful run), and
2. Had **zero** referrals in the **12 calendar months immediately before** their earliest referral in that poll window.

### Examples

| Doctor   | Last referral before today | Referral today | Alert? |
|----------|----------------------------|----------------|--------|
| Dr Smith | 14 months ago              | Yes            | Yes — nothing in last 12 months |
| Dr Jones | 11 months ago              | Yes            | No — had activity inside 12 months |
| Dr Lee   | Never                      | Yes            | Yes |
| Dr Brown | 14 months ago              | Yes (2nd today)| Yes once — dedupe by doctor |

### Lookback window

For each doctor with activity in the poll window:

- Let `first_in_window` = earliest `TEST_DATE` for that doctor in the window.
- Count distinct `TEST_RID` where `TEST_DATE >= first_in_window - 12 months` **and** `TEST_DATE < first_in_window`.
- If count = 0 → candidate.

Use the same date semantics as existing CM2 referrer reports (align timezone handling with `ReferrerHistory` / `ReferrersGrowth`).

---

## Hourly flow

```
┌─────────────────┐
│ Hourly trigger  │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Read state:     │
│ last_run_at,    │
│ notified_ids    │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Query CM2:      │
│ referrers with  │
│ tests in        │
│ (last_run, now] │
└────────┬────────┘
         ▼
┌─────────────────┐
│ For each:       │
│ prior 12-month  │
│ activity = 0?   │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Filter out      │
│ already         │
│ notified        │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Build Slack     │
│ digest (if any) │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Post to Slack   │
│ Update state    │
└─────────────────┘
```

### Poll window

Use **`last_run_at` → `now`**, not a fixed “last hour.” If a run fails or the service restarts, the next run catches up on missed referrals instead of silently skipping them.

### First deployment

On initial activation, set `last_run_at = now` and **do not backfill** historical alerts. Only forward-looking referrals trigger notifications.

---

## Slack report content

Each candidate should give enough context for a phone call without opening multiple systems.

**Proposed fields per doctor:**

| Field | Source |
|-------|--------|
| Doctor display name | Same logic as `Cm2Repository` referrer reports (`CM2_DOCTOR` / `DOCTOR` joins) |
| Practice / suburb | Practice join (as used in referrer search) |
| First referral date in window | `MIN(TEST_DATE)` in poll window |
| Investigation type(s) | `TEST_REQUIRED` mapped via `Cm2InvestigationTypeMapping` |
| Site (optional) | `SCHEDULE_SITE` if useful for routing |
| Deep link | H1Stats referrer history, e.g. `/clinical/referrer-history-cm2` with doctor id pre-filled |

**Example message:**

```
🆕 New referring doctors (no referrals in prior 12 months)

• Dr A Smith — Echo, Cardiology consult — St Vincent's — 20 Aug 2026
  https://h1stats.example/clinical/referrer-history-cm2?...

• Dr B Jones — Stress test — Mater — 20 Aug 2026
  ...
```

If no candidates in a run, **post nothing** (avoid channel noise).

---

## Deduplication and state

Without state, the same doctor could be re-alerted on every poll until business rules exclude them.

### Persisted state (proposed: `App_Data/new_referrer_alerts.json`)

```json
{
  "lastRunAt": "2026-08-20T14:00:00+10:00",
  "notified": [
    {
      "personId": "CM2:12345",
      "notifiedAt": "2026-08-20T14:00:00+10:00",
      "firstReferralDate": "2026-08-20"
    }
  ]
}
```

Use the same **`PersonId`** key as existing referrer reports:

- `CM2:{CM2_DOCTOR_RID}` when available
- `LEGACY:{REFERRING_DOCTOR}` as fallback
- Exclude `UNKNOWN`

### Business rule to confirm before build

| Policy | Behaviour |
|--------|-----------|
| **Notify once ever** (recommended for v1) | After first alert, never alert again for that `PersonId` |
| **Re-alert after return** | If doctor goes quiet 12+ months again and refers anew, alert again (requires expiring or clearing `notified` entries) |

### Failure handling

- If Slack post **fails**, do **not** advance `last_run_at` or mark doctors as notified — retry the same window on the next run.
- If CM2 query fails, log error and leave state unchanged.

---

## CM2 query approach

Reuse patterns from `Cm2Repository` (referrer growth / referrer history):

- Schema: `HEARTS1ST.TEST` (configurable via `Cm2Schema`)
- Referrer identity: `PersonId` CASE on `CM2_DOCTOR_RID` / `REFERRING_DOCTOR`
- Display name: `CM2_DOCTOR` / `DOCTOR` title + name fields
- Investigation filter: same set as referrer reporting (exclude Resting ECG type 5 per `Cm2InvestigationTypeMapping`)

### Conceptual SQL

```sql
-- Doctors with at least one test in the poll window
WITH window_tests AS (
  SELECT referring_doctor_key, MIN(test_date) AS first_in_window
  FROM ...
  WHERE test_date >= :Since AND test_date < :Until
  GROUP BY referring_doctor_key
),
prior_activity AS (
  SELECT w.referring_doctor_key
  FROM window_tests w
  WHERE EXISTS (
    SELECT 1 FROM ... t
    WHERE t.referring_doctor_key = w.referring_doctor_key
      AND t.test_date >= ADD_MONTHS(w.first_in_window, -12)
      AND t.test_date < w.first_in_window
  )
)
SELECT * FROM window_tests w
WHERE w.referring_doctor_key NOT IN (SELECT referring_doctor_key FROM prior_activity)
  AND w.referring_doctor_key <> 'UNKNOWN'
```

Implementation can be one query or two steps (candidates → batch history check); prefer matching existing repository style.

---

## Architecture (fits current H1Stats stack)

Today: ASP.NET Core API, `Cm2Repository` for Oracle reads, no background jobs, no Slack integration.

### New components (when implemented)

| Component | Responsibility |
|-----------|----------------|
| `INewReferrerAlertService` | Orchestrate query → filter → format → post → update state |
| `Cm2Repository` method | e.g. `GetNewReferrersSinceAsync(DateTime since, DateTime until)` |
| `NewReferrerAlertStateStore` | Read/write `App_Data/new_referrer_alerts.json` |
| `SlackWebhookClient` | POST formatted message to Incoming Webhook |
| Scheduler | Trigger hourly execution |

### Configuration (`appsettings`)

```json
{
  "NewReferrerAlerts": {
    "Enabled": false,
    "PollIntervalMinutes": 60,
    "SlackWebhookUrl": "",
    "FrontendBaseUrl": "https://h1stats.example",
    "LookbackMonths": 12
  }
}
```

**`Enabled: false` by default** so deployment does not activate alerts until deliberately turned on.

### Scheduling options

| Option | Pros | Cons |
|--------|------|------|
| `IHostedService` in API | Single deploy; shares DI and CM2 config | Timer tied to API process lifecycle |
| Windows Task Scheduler / cron → `POST /internal/alerts/new-referrers` | Simple ops; API stays thin | Needs secured internal endpoint |
| Hangfire / Quartz.NET | Retries, dashboard, cron expressions | New dependency |

**Recommendation for v1:** `IHostedService` with `Enabled` gate, or Task Scheduler + protected endpoint if ops prefers external scheduling.

### Slack integration

- **v1:** Incoming Webhook (simplest; one URL, no OAuth).
- **Later:** Bot token + `chat.postMessage` if threading, @mentions, or Block Kit layouts are needed.

Webhook URL must live in configuration or secrets — never commit to source control.

---

## Edge cases and decisions

| Topic | Proposed default | Notes |
|-------|------------------|-------|
| `UNKNOWN` referrers | Exclude | Cannot identify for outreach |
| Investigation types | Same filter as referrer reports | Exclude type 5 (Resting ECG) |
| Multiple sites | One alert per doctor | Aggregate types/sites in one line unless team wants per-site splits |
| Timezone | Match existing CM2 report date handling | Document choice in implementation |
| Multiple referrals same day | One Slack line per doctor | Dedupe within run by `PersonId` |
| Empty runs | No Slack message | Reduces noise |

---

## Security and access

- Slack webhook URL: secret configuration only.
- If using an HTTP trigger endpoint: restrict to localhost / internal network, or require admin API key / JWT with admin role.
- Alert job reads CM2 only (consistent with existing read-only repository pattern).
- No PHI beyond what is already shown in internal referrer reports; keep Slack content minimal (doctor name, practice, study type, date — no patient identifiers).

---

## Optional later enhancements

- **Admin UI:** Last run time, candidate preview, manual “Run now”, list of notified doctors.
- **Report page:** `/clinical/new-referrers-cm2` mirroring Slack data for users who do not use Slack.
- **Assignment:** Slack buttons or reactions to claim “I’ll call this doctor.”
- **Re-alert policy:** Configurable “notify again after N months of silence.”
- **Metrics:** Count of alerts sent per week; log to System Health page.

---

## Implementation checklist (for when ready)

- [ ] Confirm business rules: notify-once vs re-alert; investigation type filter; site grouping
- [ ] Add `GetNewReferrersSinceAsync` to `ICm2Repository` / `Cm2Repository`
- [ ] Add `NewReferrerAlertService` and state store
- [ ] Add Slack webhook client and message formatter
- [ ] Add scheduler (`IHostedService` or external trigger) gated by `Enabled: false`
- [ ] Add configuration section and document webhook setup for Slack workspace admins
- [ ] Add logging (candidates found, posted, skipped, errors)
- [ ] Manual test against CM2 with a known doctor in dev/staging
- [ ] Set `Enabled: true` and webhook URL in production when clinical team is ready

---

## Activation checklist (go-live)

1. Create dedicated Slack channel (e.g. `#new-referrer-outreach`).
2. Create Incoming Webhook for that channel.
3. Set `NewReferrerAlerts:Enabled` to `true` and configure webhook URL.
4. Deploy with `last_run_at` initialized to deployment time (no backfill).
5. Verify first hourly run in logs; confirm a test referral appears correctly (or dry-run mode if added).
6. Brief cardiologists on expected message format and dedupe behaviour.

---

## Related existing code

| Area | Location |
|------|----------|
| Referrer growth SQL / PersonId | `backend/src/H1Stats.Infrastructure/Data/Cm2Repository.cs` |
| Referrer history | `GetReferrerHistoryAsync` in same repository |
| Investigation type mapping | `backend/src/H1Stats.Core/Services/Cm2InvestigationTypeMapping.cs` |
| Referrer history UI | `frontend/src/pages/ReferrerHistoryCm2.tsx` |
| API host (no jobs today) | `backend/src/H1Stats.Api/Program.cs` |
| Local JSON state precedent | `backend/src/H1Stats.Api/App_Data/users.json` |

---

*Document created for future implementation. Do not enable in production until clinical stakeholders confirm message content, Slack channel, and notify-once policy.*
