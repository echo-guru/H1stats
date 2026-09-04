import { Fragment, useEffect, useState } from 'react'
import { RefreshCw, Save, UserX } from 'lucide-react'
import { Button } from '../components/ui/button'

interface UnassignedRow {
  patientRid: string
  patientCmId: string
  lastName: string
  firstName: string
  dateOfBirth?: string | null
  latestAppointmentId: string
  latestAppointmentDate: string
  latestAppointmentTime?: string | null
  latestServiceTitle?: string | null
  latestArrived: boolean
  latestAppointmentNote?: string | null
  roomName?: string | null
  siteName?: string | null
  bookingCount: number
}

interface UnassignedResult {
  dateFrom: string
  dateTo: string
  count: number
  rows: UnassignedRow[]
}

const CARDIOLOGIST_OPTIONS = [
  { id: '282', label: "Dr Julie Ch'ng" },
  { id: '2901', label: 'Dr Shi Yi Goo' },
  { id: '1681', label: 'Dr Vance Manins' },
  { id: '1941', label: 'Dr Justin Morze' },
  { id: '144', label: 'Dr Roess Pascoe' },
  { id: '981', label: 'Dr Jeremy Wright' },
]

function formatDisplayDate(iso?: string | null): string {
  if (!iso) return '—'
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (!m) return iso
  return `${m[3]}/${m[2]}/${m[1]}`
}

export default function UnassignedCardiologistsList() {
  const [result, setResult] = useState<UnassignedResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [expandedPatientRid, setExpandedPatientRid] = useState<string | null>(null)
  const [cardioDrafts, setCardioDrafts] = useState<Record<string, string>>({})
  const [savingId, setSavingId] = useState<string | null>(null)

  const load = async () => {
    setLoading(true)
    setError('')
    setMessage('')
    try {
      const res = await fetch('/api/sandbox/unassigned-cardiologists?lookbackMonths=12')
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to load unassigned consults')
      const parsed = data as UnassignedResult
      setResult(parsed)
      setExpandedPatientRid(null)
      setCardioDrafts({})
    } catch (e) {
      setResult(null)
      setError(e instanceof Error ? e.message : 'Failed to load unassigned consults')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const selectRow = (patientRid: string) => {
    setExpandedPatientRid((prev) => (prev === patientRid ? null : patientRid))
    setError('')
  }

  const saveAssignment = async (patientRid: string) => {
    const cardiologistId = cardioDrafts[patientRid]
    if (!cardiologistId) {
      setError('Select a cardiologist before saving')
      return
    }

    setSavingId(patientRid)
    setError('')
    setMessage('')
    try {
      const res = await fetch(
        `/api/sandbox/unassigned-cardiologists/${encodeURIComponent(patientRid)}/assign`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ cardiologistId }),
        }
      )
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to assign cardiologist')

      const name =
        typeof data.cardiologistName === 'string'
          ? data.cardiologistName
          : CARDIOLOGIST_OPTIONS.find((c) => c.id === cardiologistId)?.label || cardiologistId

      setResult((prev) =>
        prev
          ? {
              ...prev,
              count: Math.max(0, prev.count - 1),
              rows: prev.rows.filter((r) => r.patientRid !== patientRid),
            }
          : prev
      )
      setExpandedPatientRid(null)
      setMessage(`Assigned ${name} — patient removed from unassigned list`)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to assign cardiologist')
    } finally {
      setSavingId(null)
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div className="flex items-center gap-3">
          <UserX className="h-7 w-7 text-brand-primary" />
          <div>
            <h3 className="text-lg font-semibold text-brand-accent">
              Unassigned cardiologists list
            </h3>
            <p className="text-sm text-gray-600">
              Initial and follow-up consult bookings in the last 12 months (arrived and unarrived)
              where the patient has no Hearts 1st cardiologist. One row per patient.
            </p>
          </div>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={() => void load()} disabled={loading}>
          <RefreshCw className={`h-3.5 w-3.5 mr-1.5 ${loading ? 'animate-spin' : ''}`} />
          {loading ? 'Loading…' : 'Refresh'}
        </Button>
      </div>

      {error && (
        <p className="text-sm text-red-700 bg-red-50 border border-red-100 rounded-md px-3 py-2">
          {error}
        </p>
      )}
      {message && !error && (
        <p className="text-sm text-emerald-700 bg-emerald-50 border border-emerald-100 rounded-md px-3 py-2">
          {message}
        </p>
      )}

      {result && (
        <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100 text-sm text-gray-600 flex flex-wrap gap-x-4 gap-y-1">
            <span>
              <strong className="text-brand-primary">{result.count}</strong> unassigned patient
              {result.count === 1 ? '' : 's'}
            </span>
            <span>
              Window: {formatDisplayDate(result.dateFrom)} – {formatDisplayDate(result.dateTo)}
            </span>
            <span>CM2 DEV · consults only</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full table-fixed text-sm">
              <thead className="bg-brand-primary-light text-brand-primary">
                <tr>
                  <th className="text-left font-semibold px-3 py-2 w-[11%]">Lastname</th>
                  <th className="text-left font-semibold px-3 py-2 w-[11%]">Firstname</th>
                  <th className="text-left font-semibold px-3 py-2 w-[9%]">PAT ID</th>
                  <th className="text-left font-semibold px-3 py-2 w-[9%]">DOB</th>
                  <th className="text-left font-semibold px-3 py-2 w-[15%]">Latest consult</th>
                  <th className="text-left font-semibold px-3 py-2 w-[9%]">Date</th>
                  <th className="text-left font-semibold px-3 py-2 w-[7%]">Time</th>
                  <th className="text-left font-semibold px-3 py-2 w-[8%]">Arrived</th>
                  <th className="text-left font-semibold px-3 py-2 w-[21%]">Notes</th>
                </tr>
              </thead>
              <tbody>
                {result.rows.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="px-3 py-8 text-center text-gray-500">
                      No unassigned consult patients in the last 12 months.
                    </td>
                  </tr>
                ) : (
                  result.rows.map((row) => {
                    const expanded = expandedPatientRid === row.patientRid
                    const draft = cardioDrafts[row.patientRid] ?? ''
                    const saving = savingId === row.patientRid
                    const note = row.latestAppointmentNote ?? ''
                    return (
                      <Fragment key={row.patientRid}>
                        <tr
                          onClick={() => selectRow(row.patientRid)}
                          className={`border-t border-gray-100 align-top cursor-pointer ${
                            expanded ? 'bg-brand-primary-light/60' : 'hover:bg-gray-50'
                          }`}
                        >
                          <td className="px-3 py-2 font-medium text-gray-900 truncate">
                            {row.lastName || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.firstName || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.patientCmId || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-700 truncate">
                            {formatDisplayDate(row.dateOfBirth)}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.latestServiceTitle || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {formatDisplayDate(row.latestAppointmentDate)}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.latestAppointmentTime || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 truncate">
                            {row.latestArrived ? 'Yes' : 'No'}
                          </td>
                          <td className="px-3 py-2 text-gray-700">
                            <div className="truncate text-sm">
                              {note || (
                                <span className="text-gray-400 italic">Click row for full note</span>
                              )}
                            </div>
                          </td>
                        </tr>
                        {expanded ? (
                          <tr className="bg-brand-primary-light/40 border-b border-brand-primary-border">
                            <td
                              colSpan={9}
                              className="px-4 py-3"
                              onClick={(e) => e.stopPropagation()}
                            >
                              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 max-w-full">
                                <div>
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Latest consult
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {row.latestServiceTitle || '—'}
                                  </p>
                                </div>
                                <div>
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Date / time
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {formatDisplayDate(row.latestAppointmentDate)}
                                    {row.latestAppointmentTime
                                      ? ` ${row.latestAppointmentTime}`
                                      : ''}
                                  </p>
                                </div>
                                <div>
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Room
                                  </p>
                                  <p className="text-sm text-gray-800">
                                    {row.roomName || '—'}
                                    {row.siteName ? ` · ${row.siteName}` : ''}
                                  </p>
                                </div>
                                <div className="sm:col-span-2 lg:col-span-4">
                                  <p className="text-xs font-medium text-brand-primary mb-1">
                                    Notes (latest booking)
                                  </p>
                                  <div className="whitespace-pre-wrap text-sm text-gray-800 bg-white border border-gray-200 rounded-md px-3 py-2 min-h-[4.5rem]">
                                    {note || (
                                      <span className="text-gray-400 italic">No note</span>
                                    )}
                                  </div>
                                </div>
                                <div className="sm:col-span-2 lg:col-span-3">
                                  <label className="block text-xs font-medium text-brand-primary mb-1">
                                    Assign cardiologist
                                  </label>
                                  <select
                                    value={draft}
                                    disabled={saving}
                                    onChange={(e) =>
                                      setCardioDrafts((prev) => ({
                                        ...prev,
                                        [row.patientRid]: e.target.value,
                                      }))
                                    }
                                    className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-brand-primary"
                                  >
                                    <option value="">Select cardiologist…</option>
                                    {CARDIOLOGIST_OPTIONS.map((opt) => (
                                      <option key={opt.id} value={opt.id}>
                                        {opt.label}
                                      </option>
                                    ))}
                                  </select>
                                </div>
                                <div className="flex items-end">
                                  <Button
                                    type="button"
                                    size="sm"
                                    className="gap-1 h-9"
                                    disabled={saving || !draft}
                                    onClick={() => void saveAssignment(row.patientRid)}
                                  >
                                    <Save className="h-3.5 w-3.5" />
                                    {saving ? 'Saving…' : 'Save'}
                                  </Button>
                                </div>
                                <p className="sm:col-span-2 lg:col-span-4 text-xs text-gray-500">
                                  Saving sets the patient&apos;s cardiologist on CM2 DEV and removes
                                  them from this list. Click the row again to collapse.
                                </p>
                              </div>
                            </td>
                          </tr>
                        ) : null}
                      </Fragment>
                    )
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
