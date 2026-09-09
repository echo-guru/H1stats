import { Link } from 'react-router-dom'
import { Stethoscope, ArrowRight, Users, Building2, TrendingUp, TrendingDown, CalendarRange, ClipboardList, Star, BarChart3 } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useReportFavorites } from '../hooks/useReportFavorites'

interface ClinicalAction {
  title: string
  description: string
  path: string
  icon: LucideIcon
}

const REPORTING_DR_ACTIONS: ClinicalAction[] = [
  {
    title: 'Reporting Dr - Syngo',
    description: 'Physician reporting activity from H1PACS (MVF / AcusonDB).',
    path: '/clinical/physician-statistics',
    icon: Stethoscope,
  },
  {
    title: 'Reporting Dr - CM2',
    description: 'Cardiologist reporting activity from Oracle CM2 (Hearts1st.TEST).',
    path: '/clinical/reporting-dr-cm2',
    icon: Stethoscope,
  },
  {
    title: 'H1 Dr Summary (CM2)',
    description: 'Monthly OOS matrix by reporting cardiologist (Echo, DSE, SE, TEE, DCCS, Consults).',
    path: '/clinical/dr-summary-cm2',
    icon: ClipboardList,
  },
  {
    title: 'James Report',
    description: 'Site volumes by quarter — Echo, SE/DSE and consults for Greenslopes, Cleveland and Toowoomba (current + prior 2 years).',
    path: '/clinical/james-report',
    icon: BarChart3,
  },
  {
    title: 'TEE/IOE Progress Report',
    description: 'Past TEE and IOE bookings still in progress — booked, arrived, not in Syngo, awaiting a CM2 report, or a Syngo study not in CM2.',
    path: '/clinical/tee-ioe-progress',
    icon: ClipboardList,
  },
]

const REFERRING_DR_ACTIONS: ClinicalAction[] = [
  {
    title: 'Top Referring Doctors - CM2',
    description: 'Top referring doctors by person, with investigation-type breakdown (Oracle CM2).',
    path: '/clinical/top-referring-doctors-cm2',
    icon: Users,
  },
  {
    title: 'Top Referring Practices - CM2',
    description: 'Top referring practices, then doctors by provider number, then investigation types.',
    path: '/clinical/top-referring-practices-cm2',
    icon: Building2,
  },
  {
    title: 'Referrers Growth by Quarter - CM2',
    description: 'Rank referring doctors by growth versus the previous quarter.',
    path: '/clinical/referrers-growth-cm2',
    icon: TrendingUp,
  },
  {
    title: 'Referrers Decline by Quarter - CM2',
    description: 'Identify referring doctors whose referrals dropped versus the previous quarter.',
    path: '/clinical/referrers-decline-cm2',
    icon: TrendingDown,
  },
  {
    title: 'Search by referrer/practice',
    description: 'Search a referring doctor or practice and view monthly referral history for a date range.',
    path: '/clinical/referrer-history-cm2',
    icon: CalendarRange,
  },
]

const ALL_REPORTS = [...REPORTING_DR_ACTIONS, ...REFERRING_DR_ACTIONS]

function ReportCards({
  actions,
  isFavorite,
  onToggleFavorite,
}: {
  actions: ClinicalAction[]
  isFavorite: (path: string) => boolean
  onToggleFavorite: (path: string) => void
}) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      {actions.map((action) => {
        const Icon = action.icon
        const starred = isFavorite(action.path)
        return (
          <Link
            key={action.path}
            to={action.path}
            className="group bg-white rounded-lg border border-brand-primary-border shadow-sm p-5 hover:border-brand-primary hover:shadow-md transition-all"
          >
            <div className="flex items-start justify-between gap-3">
              <div className="rounded-lg bg-brand-primary-light p-2.5">
                <Icon className="h-6 w-6 text-brand-primary" />
              </div>
              <div className="flex items-center gap-1 shrink-0 mt-0.5">
                <button
                  type="button"
                  onClick={(e) => {
                    e.preventDefault()
                    e.stopPropagation()
                    onToggleFavorite(action.path)
                  }}
                  aria-label={starred ? `Remove ${action.title} from favorites` : `Add ${action.title} to favorites`}
                  aria-pressed={starred}
                  title={starred ? 'Remove from favorites' : 'Add to favorites'}
                  className="p-1 rounded-md hover:bg-amber-50"
                >
                  <Star
                    className={`h-5 w-5 ${
                      starred ? 'fill-amber-400 text-amber-500' : 'text-gray-300 group-hover:text-amber-400'
                    }`}
                  />
                </button>
                <ArrowRight className="h-5 w-5 text-gray-300 group-hover:text-brand-primary transition-colors" />
              </div>
            </div>
            <h3 className="mt-4 text-lg font-semibold text-brand-accent">{action.title}</h3>
            <p className="mt-1 text-sm text-gray-600">{action.description}</p>
          </Link>
        )
      })}
    </div>
  )
}

export default function ClinicalHome() {
  const { favorites, isFavorite, toggleFavorite } = useReportFavorites()
  const reportsByPath = Object.fromEntries(ALL_REPORTS.map((action) => [action.path, action]))
  const favoriteActions = favorites
    .map((path) => reportsByPath[path])
    .filter((action): action is ClinicalAction => Boolean(action))

  return (
    <div className="space-y-8">
      <div className="flex items-center gap-3">
        <Stethoscope className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Reports</h2>
          <p className="text-sm text-gray-600">Reporting and operational clinical tools</p>
        </div>
      </div>

      <section className="space-y-4">
        <h3 className="text-lg font-semibold text-brand-primary flex items-center gap-2">
          <Star className="h-5 w-5 fill-amber-400 text-amber-500" />
          Favorites
        </h3>
        {favoriteActions.length === 0 ? (
          <p className="text-sm text-gray-500 bg-white rounded-lg border border-dashed border-brand-primary-border px-4 py-6">
            Star reports below to pin them here. Your favorites are saved for your login.
          </p>
        ) : (
          <ReportCards
            actions={favoriteActions}
            isFavorite={isFavorite}
            onToggleFavorite={toggleFavorite}
          />
        )}
      </section>

      <hr className="border-brand-primary-border" />

      <section className="space-y-4">
        <h3 className="text-lg font-semibold text-brand-primary">Reporting Dr</h3>
        <ReportCards
          actions={REPORTING_DR_ACTIONS}
          isFavorite={isFavorite}
          onToggleFavorite={toggleFavorite}
        />
      </section>

      <hr className="border-brand-primary-border" />

      <section className="space-y-4">
        <h3 className="text-lg font-semibold text-brand-primary">Referring Dr</h3>
        <ReportCards
          actions={REFERRING_DR_ACTIONS}
          isFavorite={isFavorite}
          onToggleFavorite={toggleFavorite}
        />
      </section>
    </div>
  )
}
