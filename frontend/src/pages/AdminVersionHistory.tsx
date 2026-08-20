import { History } from 'lucide-react'
import { AdminBackLink } from '../components/AdminBackLink'
import { APP_VERSION, VERSION_HISTORY } from '../data/versionHistory'

export default function AdminVersionHistory() {
  return (
    <div className="space-y-6">
      <AdminBackLink />
      <div className="flex items-center gap-3">
        <History className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Version History</h2>
          <p className="text-sm text-gray-600">
            Current version <span className="font-semibold text-brand-primary">{APP_VERSION}</span>
          </p>
        </div>
      </div>

      <ol className="space-y-4 max-w-3xl">
        {VERSION_HISTORY.map((entry, index) => {
          const current = index === 0
          return (
            <li
              key={entry.version}
              className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-5"
            >
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <div className="flex items-center gap-2">
                  <h3 className="text-lg font-semibold text-brand-accent">v{entry.version}</h3>
                  {current && (
                    <span className="text-xs font-medium bg-brand-primary-light text-brand-primary px-2 py-0.5 rounded">
                      Current
                    </span>
                  )}
                </div>
                <time className="text-sm text-gray-500">{entry.date}</time>
              </div>
              <p className="mt-1 text-sm font-medium text-brand-primary">{entry.title}</p>
              <ul className="mt-3 space-y-1.5 list-disc pl-5 text-sm text-gray-700">
                {entry.changes.map((change) => (
                  <li key={change}>{change}</li>
                ))}
              </ul>
            </li>
          )
        })}
      </ol>
    </div>
  )
}
