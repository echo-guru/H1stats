import { useEffect, useRef, useState } from 'react'
import { ClipboardList, Download, RefreshCw, Save } from 'lucide-react'
import { Button } from '../components/ui/button'

interface SiteOption {
  id: string
  displayName: string
}

interface PinkRecallRow {
  appointmentId: string
  lastName: string
  firstName: string
  patientCmId: string
  dateOfBirth?: string | null
  cardiologistId?: string | null
  cardiologistName?: string | null
  roomName?: string | null
  siteName?: string | null
  appointmentDate: string
  appointmentTime?: string | null
  appointmentNote?: string | null
}

interface PinkRecallResult {
  dateFrom?: string | null
  dateTo?: string | null
  siteFilter?: string | null
  cardiologistId?: string | null
  count: number
  rows: PinkRecallRow[]
}

const CARDIOLOGIST_OPTIONS = [
  { id: '', label: 'All cardiologists' },
  { id: '282', label: "Dr Julie Ch'ng" },
  { id: '2901', label: 'Dr Shi Yi Goo' },
  { id: '1681', label: 'Dr Vance Manins' },
  { id: '1941', label: 'Dr Justin Morze' },
  { id: '144', label: 'Dr Roess Pascoe' },
  { id: '981', label: 'Dr Jeremy Wright' },
]

const NOTE_MAX_LENGTH = 2000

function formatDisplayDate(iso?: string | null): string {
  if (!iso) return '—'
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (!m) return iso
  return `${m[3]}/${m[2]}/${m[1]}`
}

function csvEscape(value: string): string {
  if (/[",\n\r]/.test(value)) return `"${value.replace(/"/g, '""')}"`
  return value
}

export default function WorkllistsHome() {
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [site, setSite] = useState('')
  const [sites, setSites] = useState<SiteOption[]>([])
  const [cardiologist, setCardiologist] = useState('')
  const [result, setResult] = useState<PinkRecallResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [noteDrafts, setNoteDrafts] = useState<Record<string, string>>({})
  const [editingAppointmentId, setEditingAppointmentId] = useState<string | null>(null)
  const [savingNoteId, setSavingNoteId] = useState<string | null>(null)
  const [noteMessage, setNoteMessage] = useState('')
  const noteEditorRef = useRef<HTMLTextAreaElement | null>(null)

  useEffect(() => {
    fetch('/api/clinical/cm2/sites')
      .then((r) => (r.ok ? r.json() : []))
      .then((list) => {
        if (!Array.isArray(list)) {
          setSites([])
          return
        }
        setSites(
          list.map((item: { id: string; displayName: string }) => ({
            id: String(item.id),
            displayName: item.displayName,
          }))
        )
      })
      .catch(() => setSites([]))
  }, [])

  const loadWorklist = async () => {
    const from = dateFrom.trim()
    const to = dateTo.trim()
    if ((from && !to) || (!from && to)) {
      setError('Provide both dates, or leave both blank for all unarrived pink recalls')
      return
    }

    setLoading(true)
    setError('')
    setNoteMessage('')
    try {
      const params = new URLSearchParams()
      if (from) params.set('dateFrom', from)
      if (to) params.set('dateTo', to)
      if (site) params.set('site', site)
      if (cardiologist) params.set('cardiologist', cardiologist)

      const qs = params.toString()
      const res = await fetch(`/api/workllists/pink-recalls${qs ? `?${qs}` : ''}`)
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error(err.message || 'Failed to load pink recalls')
      }
      const data: PinkRecallResult = await res.json()
      setResult(data)
      setEditingAppointmentId(null)
      setNoteDrafts(
        Object.fromEntries(
          (data.rows ?? []).map((row) => [row.appointmentId, row.appointmentNote ?? ''])
        )
      )
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load pink recalls')
      setResult(null)
      setEditingAppointmentId(null)
      setNoteDrafts({})
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (!editingAppointmentId) return
    noteEditorRef.current?.focus()
  }, [editingAppointmentId])

  const saveNote = async (appointmentId: string) => {
    const draft = noteDrafts[appointmentId] ?? ''
    const original =
      result?.rows.find((r) => r.appointmentId === appointmentId)?.appointmentNote ?? ''
    if (draft.trim() === (original ?? '').trim()) return

    if (draft.length > NOTE_MAX_LENGTH) {
      setError(`Note cannot exceed ${NOTE_MAX_LENGTH} characters`)
      return
    }

    setSavingNoteId(appointmentId)
    setError('')
    setNoteMessage('')
    try {
      const res = await fetch(
        `/api/workllists/appointments/${encodeURIComponent(appointmentId)}/note`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ note: draft }),
        }
      )
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to save note')

      const saved = typeof data.note === 'string' ? data.note : draft.trim() || null
      setResult((prev) =>
        prev
          ? {
              ...prev,
              rows: prev.rows.map((row) =>
                row.appointmentId === appointmentId
                  ? { ...row, appointmentNote: saved }
                  : row
              ),
            }
          : prev
      )
      setNoteDrafts((prev) => ({ ...prev, [appointmentId]: saved ?? '' }))
      setNoteMessage('Note saved to CM2 schedule')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save note')
    } finally {
      setSavingNoteId(null)
    }
  }

  const selectRow = async (appointmentId: string) => {
    if (editingAppointmentId === appointmentId) {
      const draft = noteDrafts[appointmentId] ?? ''
      const original =
        result?.rows.find((r) => r.appointmentId === appointmentId)?.appointmentNote ?? ''
      if (draft.trim() !== original.trim()) {
        await saveNote(appointmentId)
      }
      setEditingAppointmentId(null)
      return
    }
    if (editingAppointmentId) {
      const draft = noteDrafts[editingAppointmentId] ?? ''
      const original =
        result?.rows.find((r) => r.appointmentId === editingAppointmentId)?.appointmentNote ?? ''
      if (draft.trim() !== original.trim()) {
        await saveNote(editingAppointmentId)
      }
    }
    setEditingAppointmentId(appointmentId)
  }

  const exportCsv = () => {
    if (!result) return
    const lines = [
      'Lastname,Firstname,PAT ID,DOB,Cardiologist,Site,Room,Date,Time,Notes',
    ]
    for (const row of result.rows) {
      const note = noteDrafts[row.appointmentId] ?? row.appointmentNote ?? ''
      lines.push(
        [
          csvEscape(row.lastName || ''),
          csvEscape(row.firstName || ''),
          csvEscape(row.patientCmId || ''),
          csvEscape(formatDisplayDate(row.dateOfBirth)),
          csvEscape(row.cardiologistName || ''),
          csvEscape(row.siteName || ''),
          csvEscape(row.roomName || ''),
          csvEscape(formatDisplayDate(row.appointmentDate)),
          csvEscape(row.appointmentTime || ''),
          csvEscape(note),
        ].join(',')
      )
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `unarrived-pink-recalls-${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const rangeLabel =
    result?.dateFrom && result?.dateTo
      ? `${formatDisplayDate(result.dateFrom)} – ${formatDisplayDate(result.dateTo)}`
      : 'All unarrived'
  const siteLabel = result?.siteFilter || 'All sites'
  const cardioLabel =
    CARDIOLOGIST_OPTIONS.find((c) => c.id === (result?.cardiologistId || ''))?.label ||
    'All cardiologists'

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <ClipboardList className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Workllists</h2>
          <p className="text-sm text-gray-600">Operational worklists from the Oracle CM2 schedule</p>
        </div>
      </div>

      <section className="space-y-4">
        <div>
          <h3 className="text-lg font-semibold text-brand-primary">Unarrived pink recalls</h3>
          <p className="text-sm text-gray-600">
            Cardiology Recall diary bookings not yet arrived. Leave dates blank for all unarrived.
            Click a row to edit its notes; click again to collapse. Notes save back to the CM2 schedule.
          </p>
        </div>

        <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-4">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">Date From</label>
              <input
                type="date"
                value={dateFrom}
                onChange={(e) => setDateFrom(e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">Date To</label>
              <input
                type="date"
                value={dateTo}
                onChange={(e) => setDateTo(e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">Site</label>
              <select
                value={site}
                onChange={(e) => setSite(e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
              >
                <option value="">All sites</option>
                {sites.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.displayName}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-brand-primary mb-1">H1 Cardiologist</label>
              <select
                value={cardiologist}
                onChange={(e) => setCardiologist(e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
              >
                {CARDIOLOGIST_OPTIONS.map((c) => (
                  <option key={c.id || 'all'} value={c.id}>
                    {c.label}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="mt-4 flex flex-wrap items-center gap-2">
            <Button type="button" onClick={loadWorklist} disabled={loading}>
              <RefreshCw className={`h-4 w-4 mr-2 ${loading ? 'animate-spin' : ''}`} />
              {loading ? 'Loading…' : 'Load worklist'}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={exportCsv}
              disabled={!result || result.rows.length === 0}
            >
              <Download className="h-4 w-4 mr-2" />
              Export CSV
            </Button>
            <button
              type="button"
              className="text-sm text-gray-500 hover:text-brand-primary underline-offset-2 hover:underline"
              onClick={() => {
                setDateFrom('')
                setDateTo('')
              }}
            >
              Clear dates (all unarrived)
            </button>
          </div>

          {error && (
            <p className="mt-3 text-sm text-red-700 bg-red-50 border border-red-100 rounded-md px-3 py-2">
              {error}
            </p>
          )}
          {noteMessage && !error && (
            <p className="mt-3 text-sm text-emerald-700 bg-emerald-50 border border-emerald-100 rounded-md px-3 py-2">
              {noteMessage}
            </p>
          )}
        </div>

        {result && (
          <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm overflow-hidden">
            <div className="px-4 py-3 border-b border-gray-100 text-sm text-gray-600 flex flex-wrap gap-x-4 gap-y-1">
              <span>
                <strong className="text-brand-primary">{result.count}</strong> unarrived pink recall
                {result.count === 1 ? '' : 's'}
              </span>
              <span>Dates: {rangeLabel}</span>
              <span>Site: {siteLabel}</span>
              <span>Cardiologist: {cardioLabel}</span>
            </div>

            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead className="bg-brand-primary-light text-brand-primary">
                  <tr>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">Lastname</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">Firstname</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">PAT ID</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">DOB</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">Cardiologist</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">Room</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">Date</th>
                    <th className="text-left font-semibold px-3 py-2 whitespace-nowrap">Time</th>
                    <th className="text-left font-semibold px-3 py-2 min-w-[18rem]">Notes</th>
                  </tr>
                </thead>
                <tbody>
                  {result.rows.length === 0 ? (
                    <tr>
                      <td colSpan={9} className="px-3 py-8 text-center text-gray-500">
                        No unarrived pink recalls for the selected filters.
                      </td>
                    </tr>
                  ) : (
                    result.rows.map((row) => {
                      const draft = noteDrafts[row.appointmentId] ?? ''
                      const original = row.appointmentNote ?? ''
                      const dirty = draft.trim() !== original.trim()
                      const saving = savingNoteId === row.appointmentId
                      const editing = editingAppointmentId === row.appointmentId
                      return (
                        <tr
                          key={row.appointmentId}
                          onClick={() => void selectRow(row.appointmentId)}
                          className={`border-t border-gray-100 align-top cursor-pointer ${
                            editing
                              ? 'bg-brand-primary-light/60 ring-1 ring-inset ring-brand-primary-border'
                              : 'hover:bg-gray-50'
                          }`}
                        >
                          <td className="px-3 py-2 font-medium text-gray-900 whitespace-nowrap">
                            {row.lastName || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 whitespace-nowrap">
                            {row.firstName || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 whitespace-nowrap">
                            {row.patientCmId || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-700 whitespace-nowrap">
                            {formatDisplayDate(row.dateOfBirth)}
                          </td>
                          <td className="px-3 py-2 text-gray-800 whitespace-nowrap">
                            {row.cardiologistName || '—'}
                          </td>
                          <td className="px-3 py-2 text-gray-800 whitespace-nowrap">
                            {row.roomName || '—'}
                            {row.siteName ? (
                              <span className="block text-xs text-gray-500">{row.siteName}</span>
                            ) : null}
                          </td>
                          <td className="px-3 py-2 text-gray-800 whitespace-nowrap">
                            {formatDisplayDate(row.appointmentDate)}
                          </td>
                          <td className="px-3 py-2 text-gray-800 whitespace-nowrap">
                            {row.appointmentTime || '—'}
                          </td>
                          <td
                            className="px-3 py-2 text-gray-700 min-w-[18rem] max-w-lg"
                            onClick={(e) => {
                              if (editing) e.stopPropagation()
                            }}
                          >
                            {editing ? (
                              <>
                                <textarea
                                  ref={noteEditorRef}
                                  value={draft}
                                  maxLength={NOTE_MAX_LENGTH}
                                  rows={3}
                                  disabled={saving}
                                  onChange={(e) =>
                                    setNoteDrafts((prev) => ({
                                      ...prev,
                                      [row.appointmentId]: e.target.value,
                                    }))
                                  }
                                  onBlur={() => {
                                    if (dirty) void saveNote(row.appointmentId)
                                  }}
                                  className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary resize-y min-h-[4.5rem] bg-white"
                                  placeholder="Add schedule note…"
                                />
                                <div className="mt-1 flex items-center justify-between gap-2">
                                  <span className="text-xs text-gray-400">
                                    {draft.length}/{NOTE_MAX_LENGTH}
                                  </span>
                                  <Button
                                    type="button"
                                    variant="outline"
                                    size="sm"
                                    className="gap-1 h-7"
                                    disabled={!dirty || saving}
                                    onClick={() => void saveNote(row.appointmentId)}
                                  >
                                    <Save className="h-3.5 w-3.5" />
                                    {saving ? 'Saving…' : 'Save'}
                                  </Button>
                                </div>
                              </>
                            ) : (
                              <div className="whitespace-pre-wrap text-sm text-gray-700 min-h-[1.5rem]">
                                {draft || (
                                  <span className="text-gray-400 italic">Click row to edit notes</span>
                                )}
                              </div>
                            )}
                          </td>
                        </tr>
                      )
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </section>
    </div>
  )
}
