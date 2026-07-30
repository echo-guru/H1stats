import { Link } from 'react-router-dom'
import { Stethoscope, ArrowRight, Users, Building2 } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

interface ClinicalAction {
  title: string
  description: string
  path: string
  icon: LucideIcon
}

const CLINICAL_ACTIONS: ClinicalAction[] = [
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
]

export default function ClinicalHome() {
  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Stethoscope className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Clinical</h2>
          <p className="text-sm text-gray-600">Reporting and operational clinical tools</p>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {CLINICAL_ACTIONS.map((action) => {
          const Icon = action.icon
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
                <ArrowRight className="h-5 w-5 text-gray-300 group-hover:text-brand-primary transition-colors shrink-0 mt-1" />
              </div>
              <h3 className="mt-4 text-lg font-semibold text-brand-accent">{action.title}</h3>
              <p className="mt-1 text-sm text-gray-600">{action.description}</p>
            </Link>
          )
        })}
      </div>
    </div>
  )
}
