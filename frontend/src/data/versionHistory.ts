export interface VersionEntry {
  version: string
  date: string
  title: string
  changes: string[]
}

/** Current product version shown in Settings and System Health. */
export const APP_VERSION = '1.7.0'

export const VERSION_HISTORY: VersionEntry[] = [
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
