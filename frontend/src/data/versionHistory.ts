export interface VersionEntry {
  version: string
  date: string
  title: string
  changes: string[]
}

/** Current product version shown in Settings and System Health. */
export const APP_VERSION = '1.4.0'

export const VERSION_HISTORY: VersionEntry[] = [
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
