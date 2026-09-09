import { Fragment, useEffect, useRef, useState } from 'react'
import { CheckCircle2, ClipboardList, Download, RefreshCw, X } from 'lucide-react'
import { Button } from '../components/ui/button'
import UnactionedConsultReferralsList from './UnactionedConsultReferralsList'
import MissingCtcaDocsList from './MissingCtcaDocsList'

type WorklistTab = 'pink-recalls' | 'unactioned-consult-referrals' | 'missing-ctca-docs'

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
  serviceTypeId: string
  serviceTitle: string
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
  { id: 'unassigned', label: 'Unassigned' },
  { id: '282', label: "Dr Julie Ch'ng" },
  { id: '2901', label: 'Dr Shi Yi Goo' },
  { id: '1681', label: 'Dr Vance Manins' },
  { id: '1941', label: 'Dr Justin Morze' },
  { id: '144', label: 'Dr Roess Pascoe' },
  { id: '981', label: 'Dr Jeremy Wright' },
]

const NOTE_MAX_LENGTH = 2000

/** Matches CM2 recall OCC_SERVICE RIDs — pink Cardiology / yellow Admin. */
const SERVICE_TYPE_OPTIONS = [
  { id: '41', label: 'Cardiology Recall (pink)' },
  { id: '28', label: 'Admin Recall (yellow)' },
]

const PINK_SERVICE_TYPE_ID = '41'

function formatDisplayDate(iso?: string | null): string {
  if (!iso) return '—'
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (!m) return iso
  return `${m[3]}/${m[2]}/${m[1]}`
}

function toDateInputValue(iso?: string | null): string {
  if (!iso) return ''
  const m = /^(\d{4}-\d{2}-\d{2})/.exec(iso)
  return m ? m[1] : ''
}

function toTimeInputValue(time?: string | null): string {
  if (!time) return ''
  const m = /^(\d{1,2}):(\d{2})/.exec(time.trim())
  if (!m) return ''
  return `${m[1].padStart(2, '0')}:${m[2]}`
}

function csvEscape(value: string): string {
  if (/[",\n\r]/.test(value)) return `"${value.replace(/"/g, '""')}"`
  return value
}

export interface WorkllistsHomeProps {
  /** Base path for worklist APIs (no trailing slash). */
  apiBase?: string
  /** Sites list endpoint. */
  sitesApi?: string
  /** When false, omit the top page title block (useful when embedded). */
  showPageHeader?: boolean
  title?: string
  subtitle?: string
  noteSavedMessage?: string
  /**
   * When true, allow arrive / notes / test type / schedule edits (SandBox / CM2DEV only).
   * Production Worklists stays read-only.
   */
  allowMutations?: boolean
}

export default function WorkllistsHome({
  apiBase = '/api/workllists',
  sitesApi = '/api/clinical/cm2/sites',
  showPageHeader = true,
  title = 'Worklists',
  subtitle = 'Operational worklists from Oracle CM2 (read-only — use SandBox to test edits)',
  noteSavedMessage = 'Note saved to CM2 schedule',
  allowMutations = false,
}: WorkllistsHomeProps = {}) {
  const [tab, setTab] = useState<WorklistTab>('pink-recalls')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [site, setSite] = useState('')
  const [sites, setSites] = useState<SiteOption[]>([])
  const [cardiologist, setCardiologist] = useState('')
  const [result, setResult] = useState<PinkRecallResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [noteDrafts, setNoteDrafts] = useState<Record<string, string>>({})
  const [scheduleDrafts, setScheduleDrafts] = useState<
    Record<string, { date: string; time: string }>
  >({})
  const [serviceTypeDrafts, setServiceTypeDrafts] = useState<Record<string, string>>({})
  const [pendingArrive, setPendingArrive] = useState<Record<string, boolean>>({})
  const [editingAppointmentId, setEditingAppointmentId] = useState<string | null>(null)
  const [savingNoteId, setSavingNoteId] = useState<string | null>(null)
  const [noteMessage, setNoteMessage] = useState('')
  const [availableSlots, setAvailableSlots] = useState<string[]>([])
  const [slotsLoading, setSlotsLoading] = useState(false)
  const [slotsMessage, setSlotsMessage] = useState('')
  const noteEditorRef = useRef<HTMLTextAreaElement | null>(null)

  useEffect(() => {
    fetch(sitesApi)
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
  }, [sitesApi])

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
      const res = await fetch(`${apiBase}/pink-recalls${qs ? `?${qs}` : ''}`)
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
      setScheduleDrafts(
        Object.fromEntries(
          (data.rows ?? []).map((row) => [
            row.appointmentId,
            {
              date: toDateInputValue(row.appointmentDate),
              time: toTimeInputValue(row.appointmentTime),
            },
          ])
        )
      )
      setServiceTypeDrafts(
        Object.fromEntries(
          (data.rows ?? []).map((row) => [
            row.appointmentId,
            String(row.serviceTypeId || PINK_SERVICE_TYPE_ID),
          ])
        )
      )
      setPendingArrive({})
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load pink recalls')
      setResult(null)
      setEditingAppointmentId(null)
      setNoteDrafts({})
      setScheduleDrafts({})
      setServiceTypeDrafts({})
      setPendingArrive({})
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (!allowMutations || !editingAppointmentId) return
    noteEditorRef.current?.focus()
  }, [allowMutations, editingAppointmentId])

  // Load free room slots for the draft date while a row is expanded (mutations only).
  useEffect(() => {
    if (!allowMutations || !editingAppointmentId) {
      setAvailableSlots([])
      setSlotsMessage('')
      setSlotsLoading(false)
      return
    }

    const date = scheduleDrafts[editingAppointmentId]?.date
    if (!date) {
      setAvailableSlots([])
      setSlotsMessage('Pick a date to see free times')
      return
    }

    const appointmentId = editingAppointmentId
    let cancelled = false
    setSlotsLoading(true)
    setSlotsMessage('')

    fetch(
      `${apiBase}/appointments/${encodeURIComponent(appointmentId)}/available-slots?date=${encodeURIComponent(date)}`
    )
      .then(async (res) => {
        const data = await res.json().catch(() => ({}))
        if (!res.ok) throw new Error(data.message || 'Failed to load free slots')
        return data as {
          slots?: string[]
          message?: string | null
          currentTime?: string | null
        }
      })
      .then((data) => {
        if (cancelled) return
        const slots = Array.isArray(data.slots) ? data.slots.map(String) : []
        setAvailableSlots(slots)
        setSlotsMessage(typeof data.message === 'string' ? data.message : '')

        setScheduleDrafts((prev) => {
          const draft = prev[appointmentId]
          if (!draft) return prev
          const time = draft.time
          if (time && slots.includes(time)) return prev
          // Prefer current server time if still free; otherwise clear so user must choose.
          const nextTime =
            data.currentTime && slots.includes(data.currentTime) ? data.currentTime : ''
          if (time === nextTime) return prev
          return { ...prev, [appointmentId]: { ...draft, time: nextTime } }
        })
      })
      .catch((e) => {
        if (cancelled) return
        setAvailableSlots([])
        setSlotsMessage(e instanceof Error ? e.message : 'Failed to load free slots')
      })
      .finally(() => {
        if (!cancelled) setSlotsLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [allowMutations, apiBase, editingAppointmentId, scheduleDrafts[editingAppointmentId ?? '']?.date])

  const isRowDirty = (appointmentId: string) => {
    if (!allowMutations) return false
    const row = result?.rows.find((r) => r.appointmentId === appointmentId)
    if (!row) return false
    const note = noteDrafts[appointmentId] ?? ''
    if (note.trim() !== (row.appointmentNote ?? '').trim()) return true
    const schedule = scheduleDrafts[appointmentId]
    if (
      schedule &&
      (schedule.date !== toDateInputValue(row.appointmentDate) ||
        schedule.time !== toTimeInputValue(row.appointmentTime))
    ) {
      return true
    }
    const serviceDraft =
      serviceTypeDrafts[appointmentId] ?? String(row.serviceTypeId || PINK_SERVICE_TYPE_ID)
    if (serviceDraft !== String(row.serviceTypeId || PINK_SERVICE_TYPE_ID)) return true
    if (pendingArrive[appointmentId]) return true
    return false
  }

  const discardEdits = (appointmentId: string) => {
    const row = result?.rows.find((r) => r.appointmentId === appointmentId)
    if (!row) return
    setNoteDrafts((prev) => ({ ...prev, [appointmentId]: row.appointmentNote ?? '' }))
    setScheduleDrafts((prev) => ({
      ...prev,
      [appointmentId]: {
        date: toDateInputValue(row.appointmentDate),
        time: toTimeInputValue(row.appointmentTime),
      },
    }))
    setServiceTypeDrafts((prev) => ({
      ...prev,
      [appointmentId]: String(row.serviceTypeId || PINK_SERVICE_TYPE_ID),
    }))
    setPendingArrive((prev) => ({ ...prev, [appointmentId]: false }))
    setError('')
  }

  const removeRowFromWorklist = (appointmentId: string) => {
    setResult((prev) =>
      prev
        ? {
            ...prev,
            count: Math.max(0, prev.count - 1),
            rows: prev.rows.filter((r) => r.appointmentId !== appointmentId),
          }
        : prev
    )
    setEditingAppointmentId(null)
    setPendingArrive((prev) => ({ ...prev, [appointmentId]: false }))
  }

  /** Apply staged edits. Row stays until Done finishes; then removes only if it no longer belongs. */
  const doneEditing = async (appointmentId: string) => {
    if (!allowMutations) return
    const row = result?.rows.find((r) => r.appointmentId === appointmentId)
    if (!row) return

    const noteDraft = noteDrafts[appointmentId] ?? ''
    const scheduleDraft = scheduleDrafts[appointmentId] ?? {
      date: toDateInputValue(row.appointmentDate),
      time: toTimeInputValue(row.appointmentTime),
    }
    const serviceDraft =
      serviceTypeDrafts[appointmentId] ?? String(row.serviceTypeId || PINK_SERVICE_TYPE_ID)
    const willArrive = Boolean(pendingArrive[appointmentId])

    if (noteDraft.length > NOTE_MAX_LENGTH) {
      setError(`Note cannot exceed ${NOTE_MAX_LENGTH} characters`)
      return
    }
    if (!scheduleDraft.date) {
      setError('Appointment date is required')
      return
    }
    if (
      (scheduleDraft.date !== toDateInputValue(row.appointmentDate) ||
        scheduleDraft.time !== toTimeInputValue(row.appointmentTime)) &&
      !scheduleDraft.time
    ) {
      setError('Pick an available time slot before saving')
      return
    }

    setSavingNoteId(appointmentId)
    setError('')
    setNoteMessage('')

    const messages: string[] = []
    let nextDate = toDateInputValue(row.appointmentDate)
    let nextTime = toTimeInputValue(row.appointmentTime) || null
    let nextServiceId = String(row.serviceTypeId || PINK_SERVICE_TYPE_ID)
    let nextServiceTitle = row.serviceTitle
    let nextNote = row.appointmentNote ?? null
    let leftList = false

    try {
      // 1) Note
      if (noteDraft.trim() !== (row.appointmentNote ?? '').trim()) {
        const res = await fetch(
          `${apiBase}/appointments/${encodeURIComponent(appointmentId)}/note`,
          {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ note: noteDraft }),
          }
        )
        const data = await res.json().catch(() => ({}))
        if (!res.ok) throw new Error(data.message || 'Failed to save note')
        nextNote = typeof data.note === 'string' ? data.note : noteDraft.trim() || null
        messages.push(noteSavedMessage)
      }

      // 2) Schedule (clash-checked server-side)
      if (
        scheduleDraft.date !== toDateInputValue(row.appointmentDate) ||
        scheduleDraft.time !== toTimeInputValue(row.appointmentTime)
      ) {
        const res = await fetch(
          `${apiBase}/appointments/${encodeURIComponent(appointmentId)}/schedule`,
          {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              date: scheduleDraft.date,
              time: scheduleDraft.time || null,
            }),
          }
        )
        const data = await res.json().catch(() => ({}))
        if (!res.ok) throw new Error(data.message || 'Failed to move appointment')
        nextDate =
          typeof data.appointmentDate === 'string' ? data.appointmentDate : scheduleDraft.date
        nextTime =
          typeof data.appointmentTime === 'string'
            ? data.appointmentTime
            : scheduleDraft.time || null
        messages.push(
          `Moved to ${formatDisplayDate(nextDate)}${nextTime ? ` ${nextTime}` : ''}`
        )
      }

      // 3) Test type
      if (serviceDraft !== String(row.serviceTypeId || PINK_SERVICE_TYPE_ID)) {
        const res = await fetch(
          `${apiBase}/appointments/${encodeURIComponent(appointmentId)}/service-type`,
          {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ serviceTypeId: serviceDraft }),
          }
        )
        const data = await res.json().catch(() => ({}))
        if (!res.ok) throw new Error(data.message || 'Failed to update test type')
        nextServiceId = String(data.serviceTypeId ?? serviceDraft)
        nextServiceTitle =
          typeof data.serviceTitle === 'string' ? data.serviceTitle : nextServiceId
        messages.push(`Test type set to ${nextServiceTitle}`)
      }

      // 4) Arrive (after other edits so note/schedule land first)
      if (willArrive) {
        const res = await fetch(
          `${apiBase}/appointments/${encodeURIComponent(appointmentId)}/arrive`,
          { method: 'POST' }
        )
        const data = await res.json().catch(() => ({}))
        if (!res.ok) throw new Error(data.message || 'Failed to arrive appointment')
        messages.push('Marked as arrived')
        leftList = true
      }

      const from = dateFrom.trim()
      const to = dateTo.trim()
      const outsideRange = Boolean(from && to) && (nextDate < from || nextDate > to)
      if (!leftList && nextServiceId !== PINK_SERVICE_TYPE_ID) leftList = true
      if (!leftList && outsideRange) leftList = true

      if (leftList) {
        removeRowFromWorklist(appointmentId)
        const reason =
          willArrive
            ? 'removed from unarrived worklist'
            : nextServiceId !== PINK_SERVICE_TYPE_ID
              ? 'removed from pink recall worklist'
              : 'outside current date filter'
        setNoteMessage(`${messages.join(' · ')} — ${reason}`)
      } else {
        setResult((prev) =>
          prev
            ? {
                ...prev,
                rows: prev.rows.map((r) =>
                  r.appointmentId === appointmentId
                    ? {
                        ...r,
                        appointmentNote: nextNote,
                        appointmentDate: nextDate,
                        appointmentTime: nextTime,
                        serviceTypeId: nextServiceId,
                        serviceTitle: nextServiceTitle,
                      }
                    : r
                ),
              }
            : prev
        )
        setNoteDrafts((prev) => ({ ...prev, [appointmentId]: nextNote ?? '' }))
        setScheduleDrafts((prev) => ({
          ...prev,
          [appointmentId]: {
            date: toDateInputValue(nextDate),
            time: toTimeInputValue(nextTime),
          },
        }))
        setServiceTypeDrafts((prev) => ({ ...prev, [appointmentId]: nextServiceId }))
        setPendingArrive((prev) => ({ ...prev, [appointmentId]: false }))
        setEditingAppointmentId(null)
        setNoteMessage(
          messages.length > 0 ? messages.join(' · ') : 'No changes to apply'
        )
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to apply changes')
    } finally {
      setSavingNoteId(null)
    }
  }

  const cancelEditing = (appointmentId: string) => {
    discardEdits(appointmentId)
    setEditingAppointmentId(null)
    setNoteMessage('')
  }

  const selectRow = (appointmentId: string) => {
    if (editingAppointmentId === appointmentId) {
      // Toggle collapse — drafts kept so re-expand restores unsaved edits
      setEditingAppointmentId(null)
      return
    }
    if (allowMutations && editingAppointmentId && isRowDirty(editingAppointmentId)) {
      const leave = window.confirm(
        'You have unsaved changes on another row. Discard them and switch?'
      )
      if (!leave) return
      discardEdits(editingAppointmentId)
    }
    setEditingAppointmentId(appointmentId)
    setError('')
  }

  const exportCsv = () => {
    if (!result) return
    const lines = [
      'Lastname,Firstname,PAT ID,DOB,Cardiologist,Test type,Site,Room,Date,Time,Notes',
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
          csvEscape(row.serviceTitle || ''),
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
      {showPageHeader && (
        <div className="flex items-center gap-3">
          <ClipboardList className="h-8 w-8 text-brand-primary" />
          <div>
            <h2 className="text-2xl font-semibold text-brand-accent">{title}</h2>
            <p className="text-sm text-gray-600">{subtitle}</p>
          </div>
        </div>
      )}

      <div className="flex gap-1 border-b border-brand-primary-border">
        <button
          type="button"
          onClick={() => setTab('pink-recalls')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
            tab === 'pink-recalls'
              ? 'border-brand-primary text-brand-primary'
              : 'border-transparent text-gray-500 hover:text-brand-primary'
          }`}
        >
          Unarrived pink recalls
        </button>
        <button
          type="button"
          onClick={() => setTab('unactioned-consult-referrals')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
            tab === 'unactioned-consult-referrals'
              ? 'border-brand-primary text-brand-primary'
              : 'border-transparent text-gray-500 hover:text-brand-primary'
          }`}
        >
          Unactioned consult referrals
        </button>
        <button
          type="button"
          onClick={() => setTab('missing-ctca-docs')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
            tab === 'missing-ctca-docs'
              ? 'border-brand-primary text-brand-primary'
              : 'border-transparent text-gray-500 hover:text-brand-primary'
          }`}
        >
          Missing CTCA docs
        </button>
      </div>

      {tab === 'unactioned-consult-referrals' ? (
        <UnactionedConsultReferralsList apiBase={apiBase} />
      ) : tab === 'missing-ctca-docs' ? (
        <MissingCtcaDocsList apiBase={apiBase} />
      ) : (
      <section className="space-y-4">
        <div>
          <h3 className="text-lg font-semibold text-brand-primary">Unarrived pink recalls</h3>
          <p className="text-sm text-gray-600">
            Cardiology Recall diary bookings not yet arrived. Leave dates blank for all unarrived.
            {allowMutations
              ? ' Click a row to expand the editor (click again to collapse). Arrive, notes, test type, and schedule changes apply only when you click Done (CM2 DEV).'
              : ' Click a row to expand and read the full recall note (click again to collapse). Production is read-only — use SandBox to test edits against CM2 DEV.'}
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
              <table className="w-full table-fixed text-sm">
                <thead className="bg-brand-primary-light text-brand-primary">
                  <tr>
                    <th className="text-left font-semibold px-3 py-2 w-[9%]">Lastname</th>
                    <th className="text-left font-semibold px-3 py-2 w-[9%]">Firstname</th>
                    <th className="text-left font-semibold px-3 py-2 w-[7%]">PAT ID</th>
                    <th className="text-left font-semibold px-3 py-2 w-[7%]">DOB</th>
                    <th className="text-left font-semibold px-3 py-2 w-[11%]">Cardiologist</th>
                    <th className="text-left font-semibold px-3 py-2 w-[11%]">Test type</th>
                    <th className="text-left font-semibold px-3 py-2 w-[10%]">Room</th>
                    <th className="text-left font-semibold px-3 py-2 w-[7%]">Date</th>
                    <th className="text-left font-semibold px-3 py-2 w-[5%]">Time</th>
                    <th className="text-left font-semibold px-3 py-2 w-[24%]">Notes</th>
                  </tr>
                </thead>
                <tbody>
                  {result.rows.length === 0 ? (
                    <tr>
                      <td colSpan={10} className="px-3 py-8 text-center text-gray-500">
                        No unarrived pink recalls for the selected filters.
                      </td>
                    </tr>
                  ) : (
                    result.rows.map((row) => {
                      const draft = noteDrafts[row.appointmentId] ?? ''
                      const dirty = allowMutations && isRowDirty(row.appointmentId)
                      const saving = savingNoteId === row.appointmentId
                      const expanded = editingAppointmentId === row.appointmentId
                      const serviceDraft =
                        serviceTypeDrafts[row.appointmentId] ??
                        String(row.serviceTypeId || PINK_SERVICE_TYPE_ID)
                      const arrivePending = Boolean(pendingArrive[row.appointmentId])
                      return (
                        <Fragment key={row.appointmentId}>
                          <tr
                            onClick={() => selectRow(row.appointmentId)}
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
                              {row.cardiologistName || '—'}
                            </td>
                            <td className="px-3 py-2 text-gray-800 truncate">
                              <span
                                className={
                                  String(row.serviceTypeId) === '28'
                                    ? 'text-amber-800'
                                    : 'text-fuchsia-800'
                                }
                              >
                                {row.serviceTitle || '—'}
                              </span>
                            </td>
                            <td className="px-3 py-2 text-gray-800">
                              <span className="block truncate">{row.roomName || '—'}</span>
                              {row.siteName ? (
                                <span className="block text-xs text-gray-500 truncate">
                                  {row.siteName}
                                </span>
                              ) : null}
                            </td>
                            <td className="px-3 py-2 text-gray-800 truncate">
                              {formatDisplayDate(row.appointmentDate)}
                            </td>
                            <td className="px-3 py-2 text-gray-800 truncate">
                              {row.appointmentTime || '—'}
                            </td>
                            <td className="px-3 py-2 text-gray-700">
                              <div className="truncate text-sm text-gray-700">
                                {draft || (
                                  <span className="text-gray-400 italic">
                                    {allowMutations ? 'Click row to edit' : 'Click row for full note'}
                                  </span>
                                )}
                              </div>
                            </td>
                          </tr>
                          {expanded ? (
                            <tr className="bg-brand-primary-light/40 border-b border-brand-primary-border">
                              <td
                                colSpan={10}
                                className="px-4 py-3"
                                onClick={(e) => e.stopPropagation()}
                              >
                                {allowMutations ? (
                                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 max-w-full">
                                  <div>
                                    <label className="block text-xs font-medium text-brand-primary mb-1">
                                      Test type
                                    </label>
                                    <select
                                      value={serviceDraft}
                                      disabled={saving}
                                      onChange={(e) =>
                                        setServiceTypeDrafts((prev) => ({
                                          ...prev,
                                          [row.appointmentId]: e.target.value,
                                        }))
                                      }
                                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-brand-primary"
                                    >
                                      {SERVICE_TYPE_OPTIONS.map((opt) => (
                                        <option key={opt.id} value={opt.id}>
                                          {opt.label}
                                        </option>
                                      ))}
                                    </select>
                                  </div>
                                  <div>
                                    <label className="block text-xs font-medium text-brand-primary mb-1">
                                      Date
                                    </label>
                                    <input
                                      type="date"
                                      value={
                                        scheduleDrafts[row.appointmentId]?.date ??
                                        toDateInputValue(row.appointmentDate)
                                      }
                                      disabled={saving}
                                      onChange={(e) =>
                                        setScheduleDrafts((prev) => ({
                                          ...prev,
                                          [row.appointmentId]: {
                                            date: e.target.value,
                                            time:
                                              prev[row.appointmentId]?.time ??
                                              toTimeInputValue(row.appointmentTime),
                                          },
                                        }))
                                      }
                                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-brand-primary"
                                    />
                                  </div>
                                  <div>
                                    <label className="block text-xs font-medium text-brand-primary mb-1">
                                      Time (free slots)
                                    </label>
                                    <select
                                      value={
                                        scheduleDrafts[row.appointmentId]?.time ??
                                        toTimeInputValue(row.appointmentTime)
                                      }
                                      disabled={saving || slotsLoading || availableSlots.length === 0}
                                      onChange={(e) =>
                                        setScheduleDrafts((prev) => ({
                                          ...prev,
                                          [row.appointmentId]: {
                                            date:
                                              prev[row.appointmentId]?.date ??
                                              toDateInputValue(row.appointmentDate),
                                            time: e.target.value,
                                          },
                                        }))
                                      }
                                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-brand-primary"
                                    >
                                      <option value="">
                                        {slotsLoading
                                          ? 'Loading slots…'
                                          : availableSlots.length === 0
                                            ? 'No free slots'
                                            : 'Select time'}
                                      </option>
                                      {availableSlots.map((slot) => (
                                        <option key={slot} value={slot}>
                                          {slot}
                                        </option>
                                      ))}
                                    </select>
                                    {slotsMessage ? (
                                      <p className="mt-1 text-[11px] text-amber-800">{slotsMessage}</p>
                                    ) : (
                                      <p className="mt-1 text-[11px] text-gray-400">
                                        Free openings in this room
                                      </p>
                                    )}
                                  </div>
                                  <div className="sm:col-span-2 lg:col-span-4">
                                    <label className="block text-xs font-medium text-brand-primary mb-1">
                                      Notes
                                    </label>
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
                                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary resize-y min-h-[4.5rem] bg-white"
                                      placeholder="Add schedule note…"
                                    />
                                    <div className="mt-1.5 flex flex-wrap items-center justify-between gap-2">
                                      <span className="text-xs text-gray-400">
                                        {draft.length}/{NOTE_MAX_LENGTH}
                                        {dirty ? ' · unsaved' : ''}
                                        <span className="text-gray-400"> · click row to collapse</span>
                                      </span>
                                      <div className="flex flex-wrap items-center gap-2">
                                        <Button
                                          type="button"
                                          variant="outline"
                                          size="sm"
                                          className={`gap-1 h-7 ${
                                            arrivePending
                                              ? 'border-emerald-600 bg-emerald-50 text-emerald-800'
                                              : ''
                                          }`}
                                          disabled={saving}
                                          onClick={() =>
                                            setPendingArrive((prev) => ({
                                              ...prev,
                                              [row.appointmentId]: !prev[row.appointmentId],
                                            }))
                                          }
                                        >
                                          <CheckCircle2 className="h-3.5 w-3.5" />
                                          {arrivePending ? 'Arrive selected' : 'Arrive'}
                                        </Button>
                                        <Button
                                          type="button"
                                          variant="outline"
                                          size="sm"
                                          className="gap-1 h-7"
                                          disabled={saving}
                                          onClick={() => cancelEditing(row.appointmentId)}
                                        >
                                          <X className="h-3.5 w-3.5" />
                                          Cancel
                                        </Button>
                                        <Button
                                          type="button"
                                          size="sm"
                                          className="gap-1 h-7"
                                          disabled={saving || !dirty}
                                          onClick={() => void doneEditing(row.appointmentId)}
                                        >
                                          {saving ? 'Saving…' : 'Done'}
                                        </Button>
                                      </div>
                                    </div>
                                    <p className="mt-1 text-xs text-gray-500">
                                      Changes apply only when you click Done. Click the row again to
                                      collapse without saving.
                                    </p>
                                  </div>
                                </div>
                                ) : (
                                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 max-w-full">
                                    <div>
                                      <p className="text-xs font-medium text-brand-primary mb-1">
                                        Test type
                                      </p>
                                      <p
                                        className={`text-sm ${
                                          String(row.serviceTypeId) === '28'
                                            ? 'text-amber-800'
                                            : 'text-fuchsia-800'
                                        }`}
                                      >
                                        {row.serviceTitle || '—'}
                                      </p>
                                    </div>
                                    <div>
                                      <p className="text-xs font-medium text-brand-primary mb-1">
                                        Date
                                      </p>
                                      <p className="text-sm text-gray-800">
                                        {formatDisplayDate(row.appointmentDate)}
                                      </p>
                                    </div>
                                    <div>
                                      <p className="text-xs font-medium text-brand-primary mb-1">
                                        Time
                                      </p>
                                      <p className="text-sm text-gray-800">
                                        {row.appointmentTime || '—'}
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
                                        Notes
                                      </p>
                                      <div className="whitespace-pre-wrap text-sm text-gray-800 bg-white border border-gray-200 rounded-md px-3 py-2 min-h-[4.5rem]">
                                        {draft || (
                                          <span className="text-gray-400 italic">No note</span>
                                        )}
                                      </div>
                                      <p className="mt-1.5 text-xs text-gray-500">
                                        Read-only · click the row again to collapse
                                      </p>
                                    </div>
                                  </div>
                                )}
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
      </section>
      )}
    </div>
  )
}
