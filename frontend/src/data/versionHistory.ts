export interface VersionEntry {
  version: string
  date: string
  title: string
  changes: string[]
}

/** Current product version shown in Settings and System Health. */
export const APP_VERSION = '1.10.0'

export const VERSION_HISTORY: VersionEntry[] = [
  {
    version: '1.10.0',
    date: '29 Oct 2026',
    title: 'Christmas marketing reports and referrer filters',
    changes: [
      'Xmas marketing - by Dr: combined referral total and the practice referred from most often. CSV splits the address into street 1, street 2, suburb, and postcode.',
      'Xmas marketing - by practice: Hearts 1st practices are excluded. Filter by test type (including consults) and inpatients or outpatients. CSV is the practice total and address.',
      'Top Referring Doctors: filter by investigation type (including consults), inpatients or outpatients, and exclude Hearts 1st doctors.',
      'Referrers Growth and Decline: choose a quarter range (2 to 12 quarters) instead of only the last two.',
      'James Report: choose the date range and which tests to include, with updated charts and PDF export.',
      'Research: weekday auto-screen of today’s consults with a Slack post. GLP-1 exclusions count only a named agent the patient has taken, not a discussion or recommendation to start.',
    ],
  },
  {
    version: '1.9.0',
    date: '7 Sep 2026',
    title: 'TEE/IOE Progress Report',
    changes: [
      'Reports: TEE/IOE Progress — past Transoesophageal Echo and intra-operative TEE diary bookings that are not yet complete.',
      'Cross-checks each booking for a matching Syngo study and a CM2 TEST report; rows with both are hidden.',
      'Patient list with date range and cardiologist filters; click a row to expand booking notes. Status is booked, arrived, not in Syngo, awaiting report, or not in CM2 (Syngo study with no matching Oracle booking or TEST).',
    ],
  },
  {
    version: '1.8.0',
    date: '4 Sep 2026',
    title: 'Unactioned consult referrals worklist',
    changes: [
      'Worklists tab: Unactioned consult referrals — Cons Ref documents imported onto a patient with no unarrived Cardiology or Admin recall.',
      'Filters by import date (defaults to today) and H1 cardiologist; shows an upcoming consult booking when one exists.',
      'Click a Cons Ref title to open the referral PDF in the browser; expanded rows show consult booking notes.',
      'Worklists tab: Missing CTCA docs — past CT Coronary Angiogram bookings with no imported test/results CTCA document (one row per patient).',
    ],
  },
  {
    version: '1.7.0',
    date: '3 Sep 2026',
    title: 'Worklists diary actions and SandBox CM2DEV',
    changes: [
      'Pink recall worklist: staged Done/Cancel editing for notes, test type (Cardiology ↔ Admin Recall), arrive, and date/time moves.',
      'Move recalls only into free room slots for that day (15-minute duration-aware clash check against other bookings).',
      'Cardiologist filter includes Unassigned (null / 0 / Undefined Dr).',
      'New Super Admin SandBox tab against Oracle CM2DEV with the same worklist tools and a clear DEV-only banner.',
      'Admin Database settings and connection tests for CM2 DEV (separate from production CM2).',
    ],
  },
  {
    version: '1.6.0',
    date: '3 Sep 2026',
    title: 'Worklists and Super Admin user management',
    changes: [
      'New Super Admin–only Worklists tab with Unarrived pink recalls from the Oracle CM2 schedule.',
      'Pink recall filters: optional date range (blank = all unarrived), site, and H1 cardiologist.',
      'Click a recall row to edit schedule notes (saved back to CM2); click again to collapse to read-only.',
      'Super Admins can grant or revoke Super Admin in User Management, with last–Super Admin protection.',
    ],
  },
  {
    version: '1.5.0',
    date: '3 Sep 2026',
    title: 'Research Patient Finder screening and James Report',
    changes: [
      'Research worklist: batch Screen all, early exit on inclusion NOT MET, and suitability toggle (Likely suitable / Not suitable).',
      'Smarter screening from CM2 letters and echo reports: BMI and GLP-1/GIP facts, age-range correction, and fallback to stress echo then TTE when no consult letter exists.',
      'Likely suitable when Age, BMI, diabetes, and HbA1c are favourable even if other criteria stay UNCLEAR; criterion status colours and capitalised surnames on the list.',
      'Copy Results exports a Slack-ready summary of Suitable / Likely Suitable candidates grouped by consulting doctor.',
      'James Report (CM2): multi-year site volumes for Echo, SE/DSE, and Consults with charts and PDF export.',
    ],
  },
  {
    version: '1.4.0',
    date: '20 Aug 2026',
    title: 'User admin, practice drill-down, and report favorites',
    changes: [
      'Edit, disable, rename, and remove users in User Management, with accounts saved across API restarts.',
      'Practice search expands month → referring doctor → investigation type.',
      'Star reports on the Reports page to pin a personal Favorites section.',
      'Login list refreshes so newly created users appear without a hardcoded fallback.',
      'H1 Dr Summary (CM2) Total column with 12-month history for all Hearts 1st doctors.',
      'Dashboard: Top reporting Drs (CM2) for all six Hearts 1st cardiologists, plus a 12-month CM2 volume line chart (TEE and IOE combined).',
      'Search by referrer/practice date range and 10-year quarterly history.',
      'Version History added to Settings.',
    ],
  },
  {
    version: '1.3.0',
    date: '19 Aug 2026',
    title: 'H1 Dr Summary (CM2)',
    changes: [
      'Monthly OOS matrix by reporting cardiologist (Echo, DSE, SE, TEE, DCCS, Consults).',
      'Per-doctor 12-month history with expandable test graphs.',
    ],
  },
  {
    version: '1.2.0',
    date: '17 Aug 2026',
    title: 'Referrer growth, sites, and history search',
    changes: [
      'Referrers Growth and Decline by Quarter (CM2).',
      'Campus site filter on CM2 referring doctor reports.',
      'Search a referring doctor or practice and view monthly referral history.',
    ],
  },
  {
    version: '1.1.0',
    date: '29 Jul 2026',
    title: 'CM2 referring reports',
    changes: [
      'Oracle CM2 reporting (Hearts1st) alongside H1PACS / Syngo.',
      'Top Referring Doctors and Top Referring Practices with investigation-type breakdown.',
      'Settings overview pages and dual-server database configuration (SQL Server and Oracle).',
    ],
  },
  {
    version: '1.0.0',
    date: '8 Jul 2026',
    title: 'Platform launch',
    changes: [
      'H1Stats operational intelligence platform for Hearts 1st.',
      'Sign-in, dashboard, Reporting Dr (Syngo), user administration, and database settings.',
    ],
  },
]
