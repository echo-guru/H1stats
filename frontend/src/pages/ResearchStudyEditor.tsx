import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { FlaskConical, Plus, Trash2 } from 'lucide-react'
import { Button } from '../components/ui/button'
import { ResearchBackLink } from '../components/ResearchBackLink'

interface StudyForm {
  name: string
  description: string
  inclusionCriteria: string[]
  exclusionCriteria: string[]
  isActive: boolean
}

const EMPTY_FORM: StudyForm = {
  name: '',
  description: '',
  inclusionCriteria: [''],
  exclusionCriteria: [''],
  isActive: true,
}

function withAtLeastOne(items: string[]): string[] {
  return items.length === 0 ? [''] : items
}

export default function ResearchStudyEditor() {
  const { id } = useParams()
  const isNew = !id
  const navigate = useNavigate()
  const [form, setForm] = useState<StudyForm>(EMPTY_FORM)
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    setLoading(true)
    fetch(`/api/research/studies/${encodeURIComponent(id)}`)
      .then(async (res) => {
        if (!res.ok) throw new Error('Study not found')
        return res.json()
      })
      .then((study) => {
        setForm({
          name: study.name ?? '',
          description: study.description ?? '',
          inclusionCriteria: withAtLeastOne(study.inclusionCriteria ?? []),
          exclusionCriteria: withAtLeastOne(study.exclusionCriteria ?? []),
          isActive: Boolean(study.isActive),
        })
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load study'))
      .finally(() => setLoading(false))
  }, [id])

  const inclusionCount = useMemo(
    () => form.inclusionCriteria.filter((item) => item.trim()).length,
    [form.inclusionCriteria],
  )
  const exclusionCount = useMemo(
    () => form.exclusionCriteria.filter((item) => item.trim()).length,
    [form.exclusionCriteria],
  )

  const save = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      const payload = {
        name: form.name.trim(),
        description: form.description.trim(),
        inclusionCriteria: form.inclusionCriteria.map((item) => item.trim()).filter(Boolean),
        exclusionCriteria: form.exclusionCriteria.map((item) => item.trim()).filter(Boolean),
        isActive: form.isActive,
      }
      const res = await fetch(isNew ? '/api/research/studies' : `/api/research/studies/${encodeURIComponent(id!)}`, {
        method: isNew ? 'POST' : 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) throw new Error(data.message || 'Failed to save study')
      navigate('/research')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save study')
    } finally {
      setSaving(false)
    }
  }

  const remove = async () => {
    if (!id || !window.confirm(`Remove study "${form.name || 'this study'}"? This cannot be undone.`)) return
    setDeleting(true)
    setError('')
    try {
      const res = await fetch(`/api/research/studies/${encodeURIComponent(id)}`, { method: 'DELETE' })
      if (!res.ok) {
        const data = await res.json().catch(() => ({}))
        throw new Error(data.message || 'Failed to delete study')
      }
      navigate('/research')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete study')
    } finally {
      setDeleting(false)
    }
  }

  const updateCriteria = (kind: 'inclusionCriteria' | 'exclusionCriteria', index: number, value: string) => {
    setForm((current) => {
      const next = [...current[kind]]
      next[index] = value
      return { ...current, [kind]: next }
    })
  }

  const addCriterion = (kind: 'inclusionCriteria' | 'exclusionCriteria') => {
    setForm((current) => ({ ...current, [kind]: [...current[kind], ''] }))
  }

  const removeCriterion = (kind: 'inclusionCriteria' | 'exclusionCriteria', index: number) => {
    setForm((current) => ({
      ...current,
      [kind]: withAtLeastOne(current[kind].filter((_, i) => i !== index)),
    }))
  }

  return (
    <div className="space-y-6">
      <ResearchBackLink />
      <div className="flex items-center gap-3">
        <FlaskConical className="h-8 w-8 text-brand-primary" />
        <div>
          <h2 className="text-2xl font-semibold text-brand-accent">{isNew ? 'Add study' : 'Edit study'}</h2>
          <p className="text-sm text-gray-600">
            Name, description, and criteria that patients will later be screened against.
          </p>
        </div>
      </div>

      {error && (
        <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2 max-w-3xl">{error}</p>
      )}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading study…</p>
      ) : (
        <form onSubmit={save} className="bg-white rounded-lg border border-brand-primary-border shadow-sm p-6 space-y-6 max-w-3xl">
          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Study name</label>
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              required
              maxLength={200}
              placeholder="e.g. Heart failure registry"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-brand-primary mb-1">Description</label>
            <textarea
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              rows={4}
              maxLength={8000}
              placeholder="Short protocol summary for research staff and for the screening prompt."
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
          </div>

          <CriteriaFields
            label="Inclusion criteria"
            hint={`${inclusionCount} inclusion ${inclusionCount === 1 ? 'criterion' : 'criteria'}`}
            values={form.inclusionCriteria}
            addLabel="Add inclusion criterion"
            onChange={(index, value) => updateCriteria('inclusionCriteria', index, value)}
            onAdd={() => addCriterion('inclusionCriteria')}
            onRemove={(index) => removeCriterion('inclusionCriteria', index)}
          />

          <CriteriaFields
            label="Exclusion criteria"
            hint={`${exclusionCount} exclusion ${exclusionCount === 1 ? 'criterion' : 'criteria'}`}
            values={form.exclusionCriteria}
            addLabel="Add exclusion criterion"
            onChange={(index, value) => updateCriteria('exclusionCriteria', index, value)}
            onAdd={() => addCriterion('exclusionCriteria')}
            onRemove={(index) => removeCriterion('exclusionCriteria', index)}
          />

          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              className="rounded border-gray-300 text-brand-primary focus:ring-brand-primary"
            />
            Active — include this study when screening patients
          </label>

          <div className="flex flex-wrap gap-2 border-t border-brand-primary-border pt-4">
            <Button type="submit" variant="brand" disabled={saving}>
              {saving ? 'Saving…' : isNew ? 'Create study' : 'Save study'}
            </Button>
            <Button type="button" variant="outline" onClick={() => navigate('/research')}>
              Cancel
            </Button>
            {!isNew && (
              <Button type="button" variant="outline" onClick={() => void remove()} disabled={deleting} className="ml-auto gap-2 text-red-700">
                <Trash2 className="h-4 w-4" />
                {deleting ? 'Removing…' : 'Remove'}
              </Button>
            )}
          </div>
        </form>
      )}
    </div>
  )
}

function CriteriaFields({
  label,
  hint,
  values,
  addLabel,
  onChange,
  onAdd,
  onRemove,
}: {
  label: string
  hint: string
  values: string[]
  addLabel: string
  onChange: (index: number, value: string) => void
  onAdd: () => void
  onRemove: (index: number) => void
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-baseline justify-between gap-3">
        <label className="block text-sm font-medium text-brand-primary">{label}</label>
        <span className="text-xs text-gray-500">{hint}</span>
      </div>
      <div className="space-y-2">
        {values.map((value, index) => (
          <div key={`${label}-${index}`} className="flex gap-2">
            <textarea
              value={value}
              onChange={(e) => onChange(index, e.target.value)}
              rows={2}
              maxLength={2000}
              placeholder="One criterion per row, in protocol language."
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-brand-primary"
            />
            <Button type="button" variant="outline" onClick={() => onRemove(index)} aria-label={`Remove ${label} ${index + 1}`}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ))}
      </div>
      <Button type="button" variant="outline" size="sm" onClick={onAdd} className="gap-2">
        <Plus className="h-4 w-4" />
        {addLabel}
      </Button>
    </div>
  )
}
