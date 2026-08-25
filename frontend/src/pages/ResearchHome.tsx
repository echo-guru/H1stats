import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Microscope, CheckCircle, XCircle, RefreshCw, FlaskConical, Plus, ArrowRight, User, FileText, CalendarRange, ChevronDown, ChevronRight } from 'lucide-react'
import { Button } from '../components/ui/button'

interface LlmStatus {
  online: boolean
  label: string
  modelId?: string | null
  contextSize: number
  message: string
}

interface ResearchStudy {
  id: string
  name: string
  description: string
  inclusionCriteria: string[]
  exclusionCriteria: string[]
  isActive: boolean
  updatedAt: string
}

interface SyntheticPatient {
  id: string
  displayName: string
  notes: string
}

interface CriterionAssessment {
  criterion: string
  status: string
  reason: string
}

interface StudyScreening {
  studyId: string
  studyName: string
  overall: string
  summary: string
  inclusion: CriterionAssessment[]
  exclusion: CriterionAssessment[]
  error?: string | null
}

interface ScreeningResult {
  patient: SyntheticPatient
  studies: StudyScreening[]
  error?: string | null
  source?: string
}

interface DummyRtfPreview {
  fileName: string
  extractedText: string
  error?: string | null
}

interface UpcomingConsult {
  appointmentId: string
  patientCmId: string
  patientName: string
  appointmentDate: string
  appointmentTime?: string | null
  serviceTitle: string
  siteName?: string | null
  cardiologistId?: string | null
  cardiologistName?: string | null
  appointmentNote?: string | null
  status: string
  screenedAt?: string | null
  studies?: StudyScreening[] | null
}

interface UpcomingConsultsResult {
  dateFrom: string
  dateTo: string
  count: number
  consults: UpcomingConsult[]
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

function isoDate(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function defaultConsultRange(): { from: string; to: string } {
  const from = new Date()
  const to = new Date()
  to.setDate(to.getDate() + 13)
  return { from: isoDate(from), to: isoDate(to) }
}

function worklistStatusClass(status: string): string {
  switch (status) {
    case 'SUITABLE':
      return 'bg-emerald-50 text-emerald-800'
    case 'NOT_SUITABLE':
      return 'bg-red-50 text-red-800'
    case 'UNCLEAR':
      return 'bg-orange-50 text-orange-800'
    default:
      return 'bg-gray-100 text-gray-700'
  }
}

function worklistStatusLabel(status: string): string {
  switch (status) {
    case 'NOT_ASSESSED':
      return 'Not assessed'
    case 'SUITABLE':
      return 'Suitable'
    case 'NOT_SUITABLE':
      return 'Not suitable'
    case 'UNCLEAR':
      return 'Unclear'
    default:
      return status.replace(/_/g, ' ')
  }
}

function formatConsultDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (!m) return iso
  return `${m[3]}/${m[2]}/${m[1]}`
}

function suitabilityClass(overall: string): string {
  switch (overall) {
    case 'LIKELY_SUITABLE':
      return 'bg-emerald-50 text-emerald-800'
    case 'POSSIBLY_SUITABLE':
      return 'bg-amber-50 text-amber-800'
    case 'LIKELY_NOT_SUITABLE':
      return 'bg-red-50 text-red-800'
    default:
      return 'bg-orange-50 text-orange-800'
  }
}

function suitabilityLabel(overall: string): string {
  return overall.replace(/_/g, ' ')
}

export default function ResearchHome() {
  const [status, setStatus] = useState<LlmStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [studies, setStudies] = useState<ResearchStudy[]>([])
  const [studiesError, setStudiesError] = useState('')
  const [patient, setPatient] = useState<SyntheticPatient | null>(null)
  const [dummyRtf, setDummyRtf] = useState<DummyRtfPreview | null>(null)
  const [screening, setScreening] = useState<'notes' | 'rtf' | null>(null)
  const [screenError, setScreenError] = useState('')
  const [rtfScreenError, setRtfScreenError] = useState('')
  const [result, setResult] = useState<ScreeningResult | null>(null)
  const [rtfResult, setRtfResult] = useState<ScreeningResult | null>(null)
  const defaultRange = defaultConsultRange()
  const [consultFrom, setConsultFrom] = useState(defaultRange.from)
  const [consultTo, setConsultTo] = useState(defaultRange.to)
  const [consultCardiologist, setConsultCardiologist] = useState('')
  const [consultsLoading, setConsultsLoading] = useState(false)
  const [consultsError, setConsultsError] = useState('')
  const [consults, setConsults] = useState<UpcomingConsult[]>([])
  const [consultsLoaded, setConsultsLoaded] = useState(false)
  const [expandedAppointmentId, setExpandedAppointmentId] = useState<string | null>(null)
  const [screeningAppointmentId, setScreeningAppointmentId] = useState<string | null>(null)
  const [appointmentScreenError, setAppointmentScreenError] = useState('')

  const loadStatus = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const res = await fetch('/api/research/llm/status')
      if (!res.ok) throw new Error('Failed to check LLM status')
      const data = (await res.json()) as LlmStatus
      setStatus(data)
    } catch {
      setStatus(null)
      setError('Cannot reach the H1Stats API.')
    } finally {
      setLoading(false)
    }
  }, [])

  const loadStudies = useCallback(async () => {
    setStudiesError('')
    try {
      const res = await fetch('/api/research/studies')
      if (!res.ok) throw new Error('Failed to load studies')
      const data = (await res.json()) as ResearchStudy[]
      setStudies(Array.isArray(data) ? data : [])
    } catch (e) {
      setStudies([])
      setStudiesError(e instanceof Error ? e.message : 'Failed to load studies')
    }
  }, [])

  const loadPatient = useCallback(async () => {
    try {
      const res = await fetch('/api/research/synthetic-patient')
      if (!res.ok) throw new Error('Failed to load synthetic patient')
      setPatient((await res.json()) as SyntheticPatient)
    } catch {
      setPatient(null)
    }
  }, [])

  const loadDummyRtf = useCallback(async () => {
    try {
      const res = await fetch('/api/research/dummy-rtf')
      if (!res.ok) throw new Error('Failed to load dummy RTF')
      setDummyRtf((await res.json()) as DummyRtfPreview)
    } catch {
      setDummyRtf(null)
    }
  }, [])

  useEffect(() => {
    void loadStatus()
    void loadStudies()
    void loadPatient()
    void loadDummyRtf()
  }, [loadStatus, loadStudies, loadPatient, loadDummyRtf])

  const runScreening = async (
    path: string,
    source: 'notes' | 'rtf',
    setErr: (value: string) => void,
    setOut: (value: ScreeningResult | null) => void,
  ) => {
    setScreening(source)
    setErr('')
    try {
      const res = await fetch(path, { method: 'POST' })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Screening failed')
      setOut(data as ScreeningResult)
    } catch (e) {
      setOut(null)
      setErr(e instanceof Error ? e.message : 'Screening failed')
    } finally {
      setScreening(null)
    }
  }

  const loadUpcomingConsults = useCallback(async () => {
    setConsultsLoading(true)
    setConsultsError('')
    setAppointmentScreenError('')
    try {
      const params = new URLSearchParams({ from: consultFrom, to: consultTo })
      if (consultCardiologist) params.set('cardiologist', consultCardiologist)
      const res = await fetch(`/api/research/upcoming-consults?${params}`)
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to load upcoming consults')
      const payload = data as UpcomingConsultsResult
      setConsults(Array.isArray(payload.consults) ? payload.consults : [])
      setConsultsLoaded(true)
      setExpandedAppointmentId(null)
    } catch (e) {
      setConsults([])
      setConsultsLoaded(true)
      setConsultsError(e instanceof Error ? e.message : 'Failed to load upcoming consults')
    } finally {
      setConsultsLoading(false)
    }
  }, [consultFrom, consultTo, consultCardiologist])

  const screenAppointment = async (appointmentId: string) => {
    setScreeningAppointmentId(appointmentId)
    setAppointmentScreenError('')
    try {
      const res = await fetch(`/api/research/upcoming-consults/${encodeURIComponent(appointmentId)}/screen`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ useSampleNotes: true }),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Screening failed')
      const saved = data as {
        appointmentId: string
        status: string
        screenedAt: string
        studies: StudyScreening[]
        source?: string
      }
      setConsults((prev) =>
        prev.map((row) =>
          row.appointmentId === appointmentId
            ? {
                ...row,
                status: saved.status,
                screenedAt: saved.screenedAt,
                studies: saved.studies,
              }
            : row,
        ),
      )
      setExpandedAppointmentId(appointmentId)
    } catch (e) {
      setAppointmentScreenError(e instanceof Error ? e.message : 'Screening failed')
    } finally {
      setScreeningAppointmentId(null)
    }
  }

  const online = Boolean(status?.online)
  const activeCount = studies.filter((study) => study.isActive).length
  const busy = screening !== null || screeningAppointmentId !== null

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Microscope className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">Research</h2>
          <p className="text-sm text-gray-600">Local LLM status and research studies</p>
        </div>
      </div>

      <div className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-4 max-w-lg">
        <div className="flex items-center justify-between gap-3">
          <h3 className="text-sm font-semibold text-brand-primary">Local LLM</h3>
          <Button variant="outline" size="sm" onClick={() => void loadStatus()} disabled={loading} className="gap-2">
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>

        {loading && !status ? (
          <p className="text-sm text-muted-foreground">Checking local LLM…</p>
        ) : error ? (
          <p className="text-sm text-red-700">{error}</p>
        ) : status ? (
          <>
            <div className={`flex items-center gap-2 text-sm ${online ? 'text-emerald-700' : 'text-red-700'}`}>
              {online ? <CheckCircle className="h-4 w-4 shrink-0" /> : <XCircle className="h-4 w-4 shrink-0" />}
              <span className="font-medium">{status.label}</span>
            </div>
            {status.message && <p className="text-sm text-gray-600">{status.message}</p>}
            {online && (
              <div className="flex justify-between text-sm text-gray-600">
                <span>Context size</span>
                <span>{status.contextSize.toLocaleString()} tokens</span>
              </div>
            )}
          </>
        ) : null}
      </div>

      <section className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-4 max-w-5xl">
        <div className="flex items-start gap-3">
          <div className="rounded-lg bg-brand-primary-light p-2.5">
            <CalendarRange className="h-6 w-6 text-brand-primary" />
          </div>
          <div>
            <h3 className="text-lg font-semibold text-brand-accent">Upcoming consults</h3>
            <p className="text-sm text-gray-600">
              CM2 diary bookings for Initial and Follow-up consults. Status is saved after screening and restored on load.
            </p>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">From</label>
            <input
              type="date"
              value={consultFrom}
              onChange={(e) => setConsultFrom(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">To</label>
            <input
              type="date"
              value={consultTo}
              onChange={(e) => setConsultTo(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Cardiologist</label>
            <select
              value={consultCardiologist}
              onChange={(e) => setConsultCardiologist(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-primary bg-white"
            >
              {CARDIOLOGIST_OPTIONS.map((opt) => (
                <option key={opt.id || 'all'} value={opt.id}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
          <Button variant="brand" onClick={() => void loadUpcomingConsults()} disabled={consultsLoading || !consultFrom || !consultTo}>
            {consultsLoading ? 'Loading…' : 'Load consults'}
          </Button>
        </div>

        {consultsError && (
          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{consultsError}</p>
        )}

        {consultsLoaded && !consultsLoading && consults.length === 0 && !consultsError && (
          <p className="text-sm text-gray-500 border border-dashed border-brand-primary-border rounded-lg px-4 py-6">
            No Initial or Follow-up consult bookings in this date range.
          </p>
        )}

        {consults.length > 0 && (
          <div className="border border-brand-primary-border rounded-lg overflow-hidden">
            <div className="px-3 py-2 bg-gray-50 text-xs font-semibold text-brand-primary flex justify-between">
              <span>{consults.length} consult{consults.length === 1 ? '' : 's'}</span>
              <span>Click a row for screening detail</span>
            </div>
            <ul className="divide-y divide-gray-100">
              {consults.map((row) => {
                const open = expandedAppointmentId === row.appointmentId
                return (
                  <li key={row.appointmentId}>
                    <button
                      type="button"
                      onClick={() =>
                        setExpandedAppointmentId(open ? null : row.appointmentId)
                      }
                      className="w-full text-left px-3 py-3 hover:bg-brand-primary-light/40 transition-colors flex items-start gap-2"
                    >
                      {open ? (
                        <ChevronDown className="h-4 w-4 text-brand-primary shrink-0 mt-1" />
                      ) : (
                        <ChevronRight className="h-4 w-4 text-gray-400 shrink-0 mt-1" />
                      )}
                      <div className="min-w-0 flex-1 grid grid-cols-1 md:grid-cols-12 gap-1 md:gap-3">
                        <div className="md:col-span-3">
                          <p className="text-sm font-medium text-brand-accent truncate">{row.patientName}</p>
                          <p className="text-xs text-gray-500">PAT {row.patientCmId || '—'}</p>
                        </div>
                        <div className="md:col-span-3 text-sm text-gray-700">
                          <p>
                            {formatConsultDate(row.appointmentDate)}
                            {row.appointmentTime ? ` · ${row.appointmentTime}` : ''}
                          </p>
                          <p className="text-xs text-gray-500">{row.siteName || 'Site unknown'}</p>
                        </div>
                        <div className="md:col-span-3 text-sm text-gray-700">
                          <p className="truncate">{row.serviceTitle || 'Consult'}</p>
                          <p className="text-xs text-gray-500 truncate">
                            {row.cardiologistName || 'Cardiologist unassigned'}
                          </p>
                        </div>
                        <div className="md:col-span-3 md:text-right">
                          <span
                            className={`inline-block text-xs font-semibold px-2 py-1 rounded ${worklistStatusClass(row.status)}`}
                          >
                            {worklistStatusLabel(row.status)}
                          </span>
                        </div>
                      </div>
                    </button>
                    {open && (
                      <div
                        className="px-3 pb-4 pl-9 space-y-3 bg-gray-50/80"
                        onClick={(e) => e.stopPropagation()}
                        onKeyDown={(e) => e.stopPropagation()}
                      >
                        {row.appointmentNote && (
                          <p className="text-xs text-gray-600">
                            <span className="font-semibold text-brand-primary">Note: </span>
                            {row.appointmentNote}
                          </p>
                        )}

                        <div className="flex flex-wrap items-center gap-2">
                          <Button
                            variant="brand"
                            size="sm"
                            disabled={busy || !online || activeCount === 0}
                            onClick={() => void screenAppointment(row.appointmentId)}
                          >
                            {screeningAppointmentId === row.appointmentId
                              ? 'Screening…'
                              : row.studies?.length
                                ? 'Re-screen with sample notes'
                                : 'Screen with sample notes'}
                          </Button>
                          <span className="text-xs text-gray-500">
                            Uses Sample RTF notes until CM2 consult letters are wired. Results are saved for this appointment.
                          </span>
                        </div>

                        {screeningAppointmentId === row.appointmentId && (
                          <p className="text-sm text-gray-600">Qwen is assessing this appointment. This can take a minute or two.</p>
                        )}
                        {appointmentScreenError && expandedAppointmentId === row.appointmentId && (
                          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">
                            {appointmentScreenError}
                          </p>
                        )}

                        {row.studies && row.studies.length > 0 ? (
                          <div className="space-y-3">
                            {row.screenedAt && (
                              <p className="text-xs text-gray-500">
                                Screened {new Date(row.screenedAt).toLocaleString()}
                              </p>
                            )}
                            <ScreeningStudies studies={row.studies} />
                          </div>
                        ) : (
                          <p className="text-sm text-gray-600">
                            No screening result yet. Criteria summaries per study will appear here after assessment.
                          </p>
                        )}
                        {activeCount > 0 && !row.studies?.length && (
                          <p className="text-xs text-gray-500">
                            Active studies to assess against:{' '}
                            {studies.filter((s) => s.isActive).map((s) => s.name).join(', ')}
                          </p>
                        )}
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
          </div>
        )}
      </section>

      <section className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-4 max-w-3xl">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-start gap-3">
            <div className="rounded-lg bg-brand-primary-light p-2.5">
              <User className="h-6 w-6 text-brand-primary" />
            </div>
            <div>
              <h3 className="text-lg font-semibold text-brand-accent">Synthetic screening</h3>
              <p className="text-sm text-gray-600">
                Made-up notes only. Qwen will score this patient against every active study.
              </p>
            </div>
          </div>
          <Button
            variant="brand"
            onClick={() => void runScreening('/api/research/screen-synthetic', 'notes', setScreenError, setResult)}
            disabled={busy || !online || activeCount === 0}
          >
            {screening === 'notes' ? 'Screening…' : 'Screen synthetic patient'}
          </Button>
        </div>

        {patient && (
          <div>
            <p className="text-sm font-medium text-brand-primary mb-1">
              {patient.displayName} ({patient.id})
            </p>
            <pre className="text-xs text-gray-700 whitespace-pre-wrap bg-gray-50 border border-gray-100 rounded-lg px-3 py-2 max-h-56 overflow-auto">
              {patient.notes}
            </pre>
          </div>
        )}

        {screening === 'notes' && (
          <p className="text-sm text-gray-600">Qwen is assessing the synthetic patient. This can take a minute or two.</p>
        )}
        {screenError && (
          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{screenError}</p>
        )}

        <ScreeningStudies studies={result?.studies} />
      </section>

      <section className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-4 max-w-3xl">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-start gap-3">
            <div className="rounded-lg bg-brand-primary-light p-2.5">
              <FileText className="h-6 w-6 text-brand-primary" />
            </div>
            <div>
              <h3 className="text-lg font-semibold text-brand-accent">Dummy RTF screening</h3>
              <p className="text-sm text-gray-600">
                Word-format sample from Sample RTF, not a real CM2 report. H1Stats extracts the text, then Qwen screens it.
              </p>
            </div>
          </div>
          <Button
            variant="brand"
            onClick={() => void runScreening('/api/research/screen-dummy-rtf', 'rtf', setRtfScreenError, setRtfResult)}
            disabled={busy || !online || activeCount === 0 || !dummyRtf?.extractedText}
          >
            {screening === 'rtf' ? 'Screening…' : 'Screen dummy RTF'}
          </Button>
        </div>

        {dummyRtf?.error && (
          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{dummyRtf.error}</p>
        )}
        {dummyRtf?.extractedText && (
          <div>
            <p className="text-sm font-medium text-brand-primary mb-1">Extracted from {dummyRtf.fileName}</p>
            <pre className="text-xs text-gray-700 whitespace-pre-wrap bg-gray-50 border border-gray-100 rounded-lg px-3 py-2 max-h-56 overflow-auto">
              {dummyRtf.extractedText}
            </pre>
          </div>
        )}

        {screening === 'rtf' && (
          <p className="text-sm text-gray-600">Qwen is assessing the extracted RTF text. This can take a minute or two.</p>
        )}
        {rtfScreenError && (
          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{rtfScreenError}</p>
        )}

        <ScreeningStudies studies={rtfResult?.studies} />
      </section>

      <section className="space-y-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h3 className="text-lg font-semibold text-brand-accent">Studies</h3>
            <p className="text-sm text-gray-600">
              {studies.length === 0
                ? 'Add the first 6–8 studies patients will be screened against. More can be added later.'
                : `${activeCount} active of ${studies.length} ${studies.length === 1 ? 'study' : 'studies'}. Screening will use every active study.`}
            </p>
          </div>
          <Link to="/research/studies/new">
            <Button variant="brand" className="gap-2">
              <Plus className="h-4 w-4" />
              Add study
            </Button>
          </Link>
        </div>

        {studiesError && (
          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{studiesError}</p>
        )}

        {studies.length === 0 && !studiesError ? (
          <p className="text-sm text-gray-500 bg-white rounded-lg border border-dashed border-brand-primary-border px-4 py-6">
            No studies yet. Add a study name, description, and inclusion / exclusion criteria.
          </p>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {studies.map((study) => (
              <Link
                key={study.id}
                to={`/research/studies/${study.id}`}
                className="group bg-white rounded-lg border border-brand-primary-border shadow-sm p-5 hover:border-brand-primary hover:shadow-md transition-all"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="rounded-lg bg-brand-primary-light p-2.5">
                    <FlaskConical className="h-6 w-6 text-brand-primary" />
                  </div>
                  <ArrowRight className="h-5 w-5 text-gray-300 group-hover:text-brand-primary transition-colors shrink-0 mt-1" />
                </div>
                <h4 className="mt-4 text-lg font-semibold text-brand-accent">{study.name}</h4>
                <p className="mt-1 text-sm text-gray-600 line-clamp-2">{study.description || 'No description'}</p>
                <p className="mt-3 text-xs text-gray-500">
                  {study.isActive ? 'Active' : 'Inactive'} · {study.inclusionCriteria.length} inclusion ·{' '}
                  {study.exclusionCriteria.length} exclusion
                </p>
              </Link>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}

function ScreeningStudies({ studies }: { studies?: StudyScreening[] }) {
  if (!studies?.length) return null
  return (
    <>
      {studies.map((study) => (
        <div key={study.studyId} className="border border-brand-primary-border rounded-lg p-4 space-y-3">
          <div className="flex items-start justify-between gap-3">
            <h4 className="font-semibold text-brand-accent">{study.studyName}</h4>
            <span className={`text-xs font-semibold px-2 py-1 rounded ${suitabilityClass(study.overall)}`}>
              {suitabilityLabel(study.overall)}
            </span>
          </div>
          <p className="text-sm text-gray-700">{study.summary}</p>
          {study.error && <p className="text-sm text-red-700">{study.error}</p>}
          <CriterionList title="Inclusion" items={study.inclusion} />
          <CriterionList title="Exclusion" items={study.exclusion} />
        </div>
      ))}
    </>
  )
}

function CriterionList({ title, items }: { title: string; items: CriterionAssessment[] }) {
  if (items.length === 0) return null
  return (
    <div>
      <p className="text-xs font-semibold text-brand-primary mb-1">{title}</p>
      <ul className="space-y-1">
        {items.map((item) => (
          <li key={`${title}-${item.criterion}`} className="text-sm text-gray-700">
            <span className="font-medium">{item.status.replace(/_/g, ' ')}</span>
            {': '}
            {item.criterion}
            {item.reason ? ` — ${item.reason}` : ''}
          </li>
        ))}
      </ul>
    </div>
  )
}
