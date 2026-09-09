import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Microscope, CheckCircle, XCircle, RefreshCw, FlaskConical, Plus, ArrowRight, CalendarRange, ChevronDown, ChevronRight, Play, Square, Copy, Send } from 'lucide-react'
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

interface CriterionAssessment {
  criterion: string
  status: string
  reason: string
  evidenceSourceKind?: string | null
  evidenceSourceRid?: string | null
  evidenceFileName?: string | null
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

interface SourceDocument {
  kind: string
  patientRid?: string | null
  sourceRid?: string | null
  sourceDate?: string | null
  relativePath?: string | null
  fileName?: string | null
  title?: string | null
  reviewed: boolean
  extractError?: string | null
  consultRid?: string | null
  letterType?: number | null
  testRequired?: number | null
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
  patientRid?: string | null
  screenedAt?: string | null
  studies?: StudyScreening[] | null
  documentsReviewed?: SourceDocument[] | null
  primaryLettersRid?: string | null
  primaryLetterPath?: string | null
  primaryLetterDate?: string | null
  sourceFileFingerprint?: string | null
}

interface UpcomingConsultsResult {
  dateFrom: string
  dateTo: string
  count: number
  consults: UpcomingConsult[]
}

interface ScreeningJobItem {
  appointmentId: string
  status: string
  error?: string | null
  worklistStatus?: string | null
  screenedAt?: string | null
  alreadyReviewedSameSource?: boolean
}

interface ScreeningJob {
  id: string
  status: string
  total: number
  completed: number
  skipped: number
  failed: number
  forceRescreen: boolean
  startedAt?: string | null
  finishedAt?: string | null
  currentAppointmentId?: string | null
  error?: string | null
  items: ScreeningJobItem[]
}

type SavedScreening = {
  appointmentId: string
  status: string
  screenedAt: string
  studies: StudyScreening[]
  source?: string
  patientRid?: string | null
  documentsReviewed?: SourceDocument[] | null
  primaryLettersRid?: string | null
  primaryLetterPath?: string | null
  primaryLetterDate?: string | null
  sourceFileFingerprint?: string | null
  alreadyReviewedSameSource?: boolean
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

const BRISBANE_TZ = 'Australia/Brisbane'

/** Calendar date in Brisbane (YYYY-MM-DD). */
function brisbaneTodayIso(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: BRISBANE_TZ,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date())
}

const SLACK_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

function dayOrdinal(day: number): string {
  const mod100 = day % 100
  if (mod100 >= 11 && mod100 <= 13) return `${day}th`
  switch (day % 10) {
    case 1:
      return `${day}st`
    case 2:
      return `${day}nd`
    case 3:
      return `${day}rd`
    default:
      return `${day}th`
  }
}

/** e.g. Friday 11th Sep 2026 — Brisbane calendar date from YYYY-MM-DD. */
function candidateReportDateLabel(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso.trim())
  if (!m) return iso
  const year = Number(m[1])
  const month = Number(m[2])
  const day = Number(m[3])
  const dt = new Date(`${m[1]}-${m[2]}-${m[3]}T12:00:00+10:00`)
  const weekday = new Intl.DateTimeFormat('en-AU', {
    timeZone: BRISBANE_TZ,
    weekday: 'long',
  }).format(dt)
  return `${weekday} ${dayOrdinal(day)} ${SLACK_MONTHS[month - 1]} ${year}`
}

function defaultConsultRange(): { from: string; to: string } {
  const day = brisbaneTodayIso()
  return { from: day, to: day }
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
      return 'Likely suitable'
    case 'NOT_SUITABLE':
      return 'Not suitable'
    case 'UNCLEAR':
      return 'Unclear'
    default:
      return status.replace(/_/g, ' ')
  }
}

function documentKindLabel(kind: string): string {
  switch (kind) {
    case 'CONSULT_LETTER':
      return 'Consultation letter'
    case 'TTE_REPORT':
      return 'Transthoracic echo report'
    case 'STRESS_ECHO_REPORT':
      return 'Stress echo report'
    default:
      return kind.replace(/_/g, ' ')
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
    case 'POSSIBLY_SUITABLE':
      return 'bg-emerald-50 text-emerald-800'
    case 'LIKELY_NOT_SUITABLE':
      return 'bg-red-50 text-red-800'
    default:
      return 'bg-orange-50 text-orange-800'
  }
}

function suitabilityLabel(overall: string): string {
  switch (overall) {
    case 'LIKELY_SUITABLE':
    case 'POSSIBLY_SUITABLE':
      return 'Likely suitable'
    case 'LIKELY_NOT_SUITABLE':
      return 'Not suitable'
    case 'NEEDS_REVIEW':
      return 'Needs review'
    default:
      return overall.replace(/_/g, ' ')
  }
}

function nextReviewOverall(current: string): string {
  return current === 'LIKELY_SUITABLE' ? 'LIKELY_NOT_SUITABLE' : 'LIKELY_SUITABLE'
}

function formatListPatientName(stored: string): string {
  const trimmed = stored.trim()
  const comma = trimmed.indexOf(',')
  if (comma <= 0) return trimmed.toUpperCase()
  const last = trimmed.slice(0, comma).trim().toUpperCase()
  const first = trimmed.slice(comma + 1).trim()
  return first ? `${last}, ${first}` : last
}

function slackPatientName(stored: string): string {
  const trimmed = stored.trim()
  const comma = trimmed.indexOf(',')
  if (comma <= 0) return trimmed.toUpperCase()
  const last = trimmed.slice(0, comma).trim().toUpperCase()
  const first = trimmed.slice(comma + 1).trim()
  return first && last ? `${first} ${last}` : trimmed
}

function slackDoctorHeading(name?: string | null): string {
  const raw = (name || '').trim()
  if (!raw) return 'Unassigned'
  return /^dr\b/i.test(raw) ? raw : `Dr ${raw}`
}

function slackAppointmentDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (!m) return iso
  return `${Number(m[3])} ${SLACK_MONTHS[Number(m[2]) - 1]} ${m[1]}`
}

function slackAppointmentTime(time?: string | null): string | null {
  if (!time) return null
  const m = /^(\d{1,2}):(\d{2})/.exec(time.trim())
  if (!m) return time.trim()
  let hour = Number(m[1])
  const minute = m[2]
  const suffix = hour >= 12 ? 'pm' : 'am'
  hour = hour % 12
  if (hour === 0) hour = 12
  return `${hour}:${minute} ${suffix}`
}

/** Presentation label from stored study.overall (and existing UNCLEAR rows). Does not re-score. */
function slackStudyClassification(
  study: StudyScreening,
): 'Suitable' | 'Likely Suitable' | 'Unclear' | null {
  if (study.overall === 'LIKELY_SUITABLE') {
    const unclear = study.inclusion.concat(study.exclusion).some((row) => row.status === 'UNCLEAR')
    return unclear ? 'Likely Suitable' : 'Suitable'
  }
  if (study.overall === 'POSSIBLY_SUITABLE') return 'Likely Suitable'
  if (study.overall === 'NEEDS_REVIEW') return 'Unclear'
  return null
}

function buildCandidateSlack(consults: UpcomingConsult[], searchDateIso: string): string {
  const linesByDoctor = new Map<string, string[]>()
  const doctorOrder: string[] = []

  for (const row of consults) {
    if (row.status !== 'SUITABLE' && row.status !== 'UNCLEAR') continue

    const positive = (row.studies ?? [])
      .map((study) => {
        const classification = slackStudyClassification(study)
        return classification ? { studyName: study.studyName, classification } : null
      })
      .filter(
        (
          item,
        ): item is {
          studyName: string
          classification: 'Suitable' | 'Likely Suitable' | 'Unclear'
        } => item !== null,
      )
    if (positive.length === 0) continue

    const heading = slackDoctorHeading(row.cardiologistName)
    let lines = linesByDoctor.get(heading)
    if (!lines) {
      lines = []
      linesByDoctor.set(heading, lines)
      doctorOrder.push(heading)
    }

    const when = [slackAppointmentDate(row.appointmentDate), slackAppointmentTime(row.appointmentTime)]
      .filter(Boolean)
      .join(', ')
    const name = slackPatientName(row.patientName)
    for (const item of positive) {
      lines.push(`- ${name} — ${when} — ${item.studyName} — **${item.classification}**`)
    }
  }

  const reportDate = candidateReportDateLabel(searchDateIso)
  const parts = [`**Potential Candidate Report - ${reportDate}**`, '']
  if (doctorOrder.length === 0) {
    parts.push('No Suitable Candidates found.')
    return `${parts.join('\n')}\n`
  }

  for (const doctor of doctorOrder) {
    parts.push(`**${doctor}**`)
    parts.push(...(linesByDoctor.get(doctor) ?? []))
    parts.push('')
  }
  return `${parts.join('\n').trimEnd()}\n`
}

export default function ResearchHome() {
  const [status, setStatus] = useState<LlmStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [studies, setStudies] = useState<ResearchStudy[]>([])
  const [studiesError, setStudiesError] = useState('')
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
  const [batchJob, setBatchJob] = useState<ScreeningJob | null>(null)
  const [batchError, setBatchError] = useState('')
  const [rescreenAlreadyAssessed, setRescreenAlreadyAssessed] = useState(false)
  const [copyResultsLabel, setCopyResultsLabel] = useState<'idle' | 'copied'>('idle')
  const [slackConfigured, setSlackConfigured] = useState(false)
  const [slackSendLabel, setSlackSendLabel] = useState<'idle' | 'sending' | 'sent'>('idle')
  const appliedBatchItems = useRef<Set<string>>(new Set())
  const copyResultsTimer = useRef<number>(0)
  const slackSendTimer = useRef<number>(0)

  useEffect(
    () => () => {
      window.clearTimeout(copyResultsTimer.current)
      window.clearTimeout(slackSendTimer.current)
    },
    [],
  )

  const loadStatus = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const [llmRes, slackRes] = await Promise.all([
        fetch('/api/research/llm/status'),
        fetch('/api/research/slack/status'),
      ])
      if (!llmRes.ok) throw new Error('Failed to check LLM status')
      const data = (await llmRes.json()) as LlmStatus
      setStatus(data)
      if (slackRes.ok) {
        const slack = (await slackRes.json()) as { configured?: boolean }
        setSlackConfigured(Boolean(slack.configured))
      } else {
        setSlackConfigured(false)
      }
    } catch {
      setStatus(null)
      setSlackConfigured(false)
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

  useEffect(() => {
    void loadStatus()
    void loadStudies()
  }, [loadStatus, loadStudies])

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

  const applySavedScreening = useCallback((saved: SavedScreening) => {
    setConsults((prev) =>
      prev.map((row) =>
        row.appointmentId === saved.appointmentId
          ? {
              ...row,
              status: saved.status,
              screenedAt: saved.screenedAt,
              studies: saved.studies,
              patientRid: saved.patientRid ?? row.patientRid,
              documentsReviewed: saved.documentsReviewed ?? row.documentsReviewed,
              primaryLettersRid: saved.primaryLettersRid ?? row.primaryLettersRid,
              primaryLetterPath: saved.primaryLetterPath ?? row.primaryLetterPath,
              primaryLetterDate: saved.primaryLetterDate ?? row.primaryLetterDate,
              sourceFileFingerprint: saved.sourceFileFingerprint ?? row.sourceFileFingerprint,
            }
          : row,
      ),
    )
  }, [])

  const pullFinishedBatchResults = useCallback(
    async (job: ScreeningJob) => {
      const finished = job.items.filter(
        (item) =>
          (item.status === 'DONE' || item.status === 'SKIPPED') &&
          !appliedBatchItems.current.has(`${job.id}:${item.appointmentId}`),
      )
      for (const item of finished) {
        appliedBatchItems.current.add(`${job.id}:${item.appointmentId}`)
        try {
          const res = await fetch(
            `/api/research/upcoming-consults/${encodeURIComponent(item.appointmentId)}/screening`,
          )
          if (!res.ok) continue
          const saved = (await res.json()) as SavedScreening
          applySavedScreening(saved)
        } catch {
          /* keep polling; the next pass can retry */
          appliedBatchItems.current.delete(`${job.id}:${item.appointmentId}`)
        }
      }
    },
    [applySavedScreening],
  )

  const loadCurrentBatchJob = useCallback(async () => {
    const res = await fetch('/api/research/screening-jobs/current')
    if (!res.ok) return null
    const job = (await res.json()) as ScreeningJob
    if (!job?.id) {
      setBatchJob(job?.status && job.status !== 'IDLE' ? job : null)
      return job
    }
    setBatchJob(job)
    await pullFinishedBatchResults(job)
    return job
  }, [pullFinishedBatchResults])

  useEffect(() => {
    void loadCurrentBatchJob()
  }, [loadCurrentBatchJob])

  useEffect(() => {
    if (batchJob?.status !== 'RUNNING') return undefined
    const timer = window.setInterval(() => {
      void loadCurrentBatchJob()
    }, 2000)
    return () => window.clearInterval(timer)
  }, [batchJob?.status, loadCurrentBatchJob])

  const screenAppointment = async (appointmentId: string, opts?: { force?: boolean; full?: boolean }) => {
    setScreeningAppointmentId(appointmentId)
    setAppointmentScreenError('')
    try {
      const res = await fetch(`/api/research/upcoming-consults/${encodeURIComponent(appointmentId)}/screen`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          forceRescreen: Boolean(opts?.force),
          fullScreen: Boolean(opts?.full),
        }),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Screening failed')
      applySavedScreening(data as SavedScreening)
      setExpandedAppointmentId(appointmentId)
    } catch (e) {
      setAppointmentScreenError(e instanceof Error ? e.message : 'Screening failed')
    } finally {
      setScreeningAppointmentId(null)
    }
  }

  const startBatchScreen = async () => {
    setBatchError('')
    setAppointmentScreenError('')
    const ids = rescreenAlreadyAssessed
      ? consults.map((row) => row.appointmentId)
      : consults.filter((row) => !row.studies?.length).map((row) => row.appointmentId)
    if (ids.length === 0) {
      setBatchError(
        rescreenAlreadyAssessed
          ? 'There are no consults in this list to screen.'
          : 'Every consult in this list is already screened. Tick “Re-screen already assessed” to run them again.',
      )
      return
    }
    appliedBatchItems.current = new Set()
    setCopyResultsLabel('idle')
    window.clearTimeout(copyResultsTimer.current)
    try {
      const res = await fetch('/api/research/screening-jobs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          appointmentIds: ids,
          forceRescreen: rescreenAlreadyAssessed,
        }),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Could not start batch screening')
      const job = (data as ScreeningJob) ?? (data.job as ScreeningJob)
      setBatchJob(job)
    } catch (e) {
      setBatchError(e instanceof Error ? e.message : 'Could not start batch screening')
    }
  }

  const cancelBatchScreen = async () => {
    setBatchError('')
    try {
      const res = await fetch('/api/research/screening-jobs/current/cancel', { method: 'POST' })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Could not cancel batch')
      setBatchJob(data as ScreeningJob)
    } catch (e) {
      setBatchError(e instanceof Error ? e.message : 'Could not cancel batch')
    }
  }

  const canShareResults =
    consultsLoaded && consults.length > 0 && batchJob?.status !== 'RUNNING'

  const copyBatchResults = async () => {
    if (!canShareResults) return
    const text = buildCandidateSlack(consults, consultFrom)
    try {
      await navigator.clipboard.writeText(text)
      setCopyResultsLabel('copied')
      window.clearTimeout(copyResultsTimer.current)
      copyResultsTimer.current = window.setTimeout(() => setCopyResultsLabel('idle'), 2000)
    } catch {
      setBatchError('Could not copy results to the clipboard.')
    }
  }

  const sendBatchResultsToSlack = async () => {
    if (!canShareResults || !slackConfigured) return
    setBatchError('')
    setSlackSendLabel('sending')
    const text = buildCandidateSlack(consults, consultFrom)
    try {
      const res = await fetch('/api/research/slack/candidate-report', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text }),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Could not send to Slack')
      setSlackSendLabel('sent')
      window.clearTimeout(slackSendTimer.current)
      slackSendTimer.current = window.setTimeout(() => setSlackSendLabel('idle'), 2000)
    } catch (e) {
      setSlackSendLabel('idle')
      setBatchError(e instanceof Error ? e.message : 'Could not send to Slack')
    }
  }

  const online = Boolean(status?.online)
  const activeCount = studies.filter((study) => study.isActive).length
  const batchBusy = batchJob?.status === 'RUNNING'
  const busy = screeningAppointmentId !== null || batchBusy
  const remainingCount = consults.filter((row) => !row.studies?.length).length
  const batchTargetCount = rescreenAlreadyAssessed ? consults.length : remainingCount
  const batchProcessed = batchJob ? batchJob.completed + batchJob.skipped + batchJob.failed : 0
  const currentBatchName = batchJob?.currentAppointmentId
    ? formatListPatientName(
        consults.find((row) => row.appointmentId === batchJob.currentAppointmentId)?.patientName || '',
      )
    : ''

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
              CM2 diary bookings for Initial and Follow-up consults. Screening reads the most recent previous
              consultation letter (read-only) and saves the result against that LETTER_PATH.
            </p>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">From</label>
            <input
              type="date"
              value={consultFrom}
              onChange={(e) => {
                const day = e.target.value
                setConsultFrom(day)
                setConsultTo(day)
              }}
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
            <div className="px-3 py-2 bg-gray-50 text-xs font-semibold text-brand-primary flex flex-wrap items-center justify-between gap-2">
              <span>{consults.length} consult{consults.length === 1 ? '' : 's'}</span>
              <div className="flex flex-wrap items-center gap-2">
                <label className="flex items-center gap-1.5 font-normal text-gray-600">
                  <input
                    type="checkbox"
                    checked={rescreenAlreadyAssessed}
                    onChange={(e) => setRescreenAlreadyAssessed(e.target.checked)}
                    disabled={batchBusy}
                    className="rounded border-gray-300 text-brand-primary focus:ring-brand-primary"
                  />
                  Re-screen already assessed
                </label>
                {canShareResults && (
                  <>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="gap-1.5"
                      onClick={() => void copyBatchResults()}
                    >
                      <Copy className="h-3.5 w-3.5" />
                      {copyResultsLabel === 'copied' ? 'Copied!' : 'Copy Results'}
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="gap-1.5"
                      disabled={!slackConfigured || slackSendLabel === 'sending'}
                      title={
                        slackConfigured
                          ? 'Post this report to the configured Slack channel'
                          : 'Configure ResearchSlack:WebhookUrl on the H1Stats server'
                      }
                      onClick={() => void sendBatchResultsToSlack()}
                    >
                      <Send className="h-3.5 w-3.5" />
                      {slackSendLabel === 'sending'
                        ? 'Sending…'
                        : slackSendLabel === 'sent'
                          ? 'Sent!'
                          : 'Send to Slack'}
                    </Button>
                  </>
                )}
                {batchBusy ? (
                  <Button variant="outline" size="sm" className="gap-1.5" onClick={() => void cancelBatchScreen()}>
                    <Square className="h-3.5 w-3.5" />
                    Cancel batch
                  </Button>
                ) : (
                  <Button
                    variant="brand"
                    size="sm"
                    className="gap-1.5"
                    disabled={!online || activeCount === 0 || busy || batchTargetCount === 0}
                    onClick={() => void startBatchScreen()}
                  >
                    <Play className="h-3.5 w-3.5" />
                    Screen all{batchTargetCount > 0 ? ` (${batchTargetCount})` : ''}
                  </Button>
                )}
              </div>
            </div>

            {(batchBusy || (batchJob && batchJob.status !== 'IDLE')) && batchJob?.id ? (
              <div className="px-3 py-2 border-t border-brand-primary-border bg-white space-y-1.5">
                <div className="flex justify-between gap-3 text-xs text-gray-700 items-center">
                  <span>
                    {batchBusy
                      ? `Screening ${Math.min(batchProcessed + 1, batchJob.total)} of ${batchJob.total}${currentBatchName ? ` — ${currentBatchName}` : ''}`
                      : batchJob.status === 'CANCELLED'
                        ? `Batch cancelled after ${batchProcessed} of ${batchJob.total}`
                        : `Batch finished: ${batchJob.completed} screened, ${batchJob.skipped} skipped, ${batchJob.failed} failed`}
                  </span>
                  <span className="text-gray-500 shrink-0">
                    {batchJob.completed} done · {batchJob.skipped} skipped · {batchJob.failed} failed
                  </span>
                </div>
                <div className="h-1.5 rounded-full bg-gray-200 overflow-hidden">
                  <div
                    className="h-full bg-brand-primary transition-all"
                    style={{
                      width: `${batchJob.total === 0 ? 0 : Math.round((batchProcessed / batchJob.total) * 100)}%`,
                    }}
                  />
                </div>
                {batchJob.error && <p className="text-xs text-red-700">{batchJob.error}</p>}
                {batchBusy && (
                  <p className="text-xs text-gray-500">
                    Running in the background on the H1Stats API. You can keep using this page; results appear as each
                    consult finishes.
                  </p>
                )}
              </div>
            ) : null}

            {batchError && (
              <p className="px-3 py-2 text-sm text-red-600 bg-red-50 border-t border-red-100">{batchError}</p>
            )}

            <ul className="divide-y divide-gray-100">
              {consults.map((row) => {
                const open = expandedAppointmentId === row.appointmentId
                const batchItem = batchJob?.items.find((item) => item.appointmentId === row.appointmentId)
                const rowScreening =
                  screeningAppointmentId === row.appointmentId || batchItem?.status === 'RUNNING'
                const rowQueued = batchBusy && batchItem?.status === 'PENDING'
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
                          <p className="text-sm font-medium text-brand-accent truncate">{formatListPatientName(row.patientName)}</p>
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
                            onClick={() =>
                              void screenAppointment(row.appointmentId, {
                                force: Boolean(row.studies?.length),
                                full: Boolean(row.studies?.length),
                              })
                            }
                          >
                            {rowScreening
                              ? 'Screening…'
                              : rowQueued
                                ? 'Queued in batch'
                                : row.studies?.length
                                  ? 'Re-screen for suitability'
                                  : 'Screen for suitability'}
                          </Button>
                          <span className="text-xs text-gray-500">
                            Stops after any inclusion criterion is not met. UNCLEAR rows still pull extra reports.
                            Re-screen runs a full assessment of every criterion.
                          </span>
                        </div>

                        {rowScreening && (
                          <p className="text-sm text-gray-600">Qwen is assessing this appointment. This can take a minute or two.</p>
                        )}
                        {batchItem?.status === 'FAILED' && batchItem.error && (
                          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">
                            {batchItem.error}
                          </p>
                        )}
                        {appointmentScreenError && expandedAppointmentId === row.appointmentId && (
                          <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">
                            {appointmentScreenError}
                          </p>
                        )}

                        {(row.primaryLetterPath || (row.documentsReviewed && row.documentsReviewed.length > 0)) && (
                          <div className="rounded-lg border border-brand-primary-border bg-white px-3 py-2 space-y-1">
                            <p className="text-xs font-semibold text-brand-primary">Documents reviewed</p>
                            {row.primaryLetterPath && (
                              <p className="text-xs text-gray-700">
                                Letter {row.primaryLettersRid ? `#${row.primaryLettersRid}` : ''}
                                {row.primaryLetterDate ? ` · ${formatConsultDate(row.primaryLetterDate)}` : ''}
                                {' · '}
                                <span className="font-mono">{row.primaryLetterPath}</span>
                              </p>
                            )}
                            {row.patientRid && (
                              <p className="text-xs text-gray-500">PATIENT_RID {row.patientRid}</p>
                            )}
                            {row.documentsReviewed?.map((doc) => (
                              <p key={`${doc.kind}-${doc.sourceRid}-${doc.relativePath}`} className="text-xs text-gray-600">
                                {documentKindLabel(doc.kind)}
                                {doc.reviewed ? '' : ' (not read)'}
                                {doc.fileName ? ` · ${doc.fileName}` : ''}
                                {doc.sourceDate ? ` · ${formatConsultDate(doc.sourceDate)}` : ''}
                                {doc.extractError ? ` — ${doc.extractError}` : ''}
                              </p>
                            ))}
                          </div>
                        )}

                        {row.studies && row.studies.length > 0 ? (
                          <div className="space-y-3">
                            {row.screenedAt && (
                              <p className="text-xs text-gray-500">
                                Screened {new Date(row.screenedAt).toLocaleString()}
                                {row.sourceFileFingerprint ? ' · source file recorded' : ''}
                              </p>
                            )}
                            <ScreeningStudies
                              appointmentId={row.appointmentId}
                              studies={row.studies}
                              onUpdated={applySavedScreening}
                            />
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

function ScreeningStudies({
  appointmentId,
  studies,
  onUpdated,
}: {
  appointmentId: string
  studies?: StudyScreening[]
  onUpdated: (saved: SavedScreening) => void
}) {
  const [pendingStudyId, setPendingStudyId] = useState<string | null>(null)
  const [overrideError, setOverrideError] = useState('')

  if (!studies?.length) return null

  const toggleSuitability = async (study: StudyScreening) => {
    setPendingStudyId(study.studyId)
    setOverrideError('')
    try {
      const res = await fetch(
        `/api/research/upcoming-consults/${encodeURIComponent(appointmentId)}/screening/suitability`,
        {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            studyId: study.studyId,
            overall: nextReviewOverall(study.overall),
          }),
        },
      )
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Could not update suitability')
      onUpdated(data as SavedScreening)
    } catch (e) {
      setOverrideError(e instanceof Error ? e.message : 'Could not update suitability')
    } finally {
      setPendingStudyId(null)
    }
  }

  return (
    <>
      {overrideError && (
        <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{overrideError}</p>
      )}
      {studies.map((study) => (
        <div key={study.studyId} className="border border-brand-primary-border rounded-lg p-4 space-y-3">
          <div className="flex items-start justify-between gap-3">
            <h4 className="font-semibold text-brand-accent">{study.studyName}</h4>
            <button
              type="button"
              title="Click to mark Suitable or Not suitable"
              disabled={pendingStudyId === study.studyId}
              onClick={() => void toggleSuitability(study)}
              className={`text-xs font-semibold px-2 py-1 rounded ${suitabilityClass(study.overall)} hover:ring-2 hover:ring-brand-primary/40 disabled:opacity-60`}
            >
              {pendingStudyId === study.studyId ? 'Saving…' : suitabilityLabel(study.overall)}
            </button>
          </div>
          <p className="text-sm text-gray-700">{study.summary}</p>
          {study.error && <p className="text-sm text-red-700">{study.error}</p>}
          <CriterionList title="Inclusion" items={study.inclusion} />
          <CriterionList title="Exclusion" items={study.exclusion} isExclusion />
        </div>
      ))}
    </>
  )
}

function criterionStatusClass(status: string, isExclusion: boolean): string {
  if (status === 'UNCLEAR') return 'text-orange-800'
  const favourable = isExclusion ? status === 'NOT_MET' : status === 'MET'
  return favourable ? 'text-emerald-700' : 'text-red-700'
}

function CriterionList({
  title,
  items,
  isExclusion = false,
}: {
  title: string
  items: CriterionAssessment[]
  isExclusion?: boolean
}) {
  if (items.length === 0) return null
  return (
    <div>
      <p className="text-xs font-semibold text-brand-primary mb-1">{title}</p>
      <ul className="space-y-1">
        {items.map((item) => (
          <li key={`${title}-${item.criterion}`} className="text-sm text-gray-700">
            <span className={`font-semibold ${criterionStatusClass(item.status, isExclusion)}`}>
              {item.status.replace(/_/g, ' ')}
            </span>
            {': '}
            {item.criterion}
            {item.reason ? ` — ${item.reason}` : ''}
            {item.evidenceFileName || item.evidenceSourceKind ? (
              <span className="text-xs text-gray-500">
                {' '}
                [{item.evidenceFileName || documentKindLabel(item.evidenceSourceKind || '')}]
              </span>
            ) : null}
          </li>
        ))}
      </ul>
    </div>
  )
}
