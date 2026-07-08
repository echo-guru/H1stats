import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

const NAME_TITLES = new Set([
  'DR', 'MR', 'MRS', 'MS', 'MISS', 'PROF', 'A/PROF', 'PROFESSOR', 'SIR', 'LADY',
])

function isNameTitle(part: string): boolean {
  const normalized = part.trim().replace(/\./g, '').toUpperCase()
  return NAME_TITLES.has(normalized)
}

export function formatPersonName(name: string): string {
  if (!name) return ''
  const trimmed = name.trim()

  // HL7 / DICOM: Family^Given^Middle^Suffix^Prefix (e.g. PASCOE^ROESS^^DR)
  if (trimmed.includes('^')) {
    const components = trimmed.split('^').map((part) => part.trim())
    const family = components[0] || ''
    const givenAndMiddle = components
      .slice(1, 3)
      .filter((part) => part && !isNameTitle(part))
    const firstNames = toTitleCase(givenAndMiddle.join(' '))
    if (firstNames && family) return `${firstNames} ${family.toUpperCase()}`
    if (family) return family.toUpperCase()
    return trimmed.replace(/\^+/g, ' ').replace(/\s+/g, ' ').trim()
  }

  if (trimmed.includes(',')) {
    const [surname, rest] = trimmed.split(',')
    const firstNames = toTitleCase(rest.trim())
    return `${firstNames} ${surname.trim().toUpperCase()}`
  }

  const parts = trimmed.split(/\s+/).filter(Boolean)
  if (parts.length === 1) return parts[0].toUpperCase()
  const surname = parts[parts.length - 1].toUpperCase()
  const firstNames = toTitleCase(parts.slice(0, -1).join(' '))
  return `${firstNames} ${surname}`
}

function toTitleCase(str: string): string {
  return str.replace(/\w\S*/g, (txt) => txt.charAt(0).toUpperCase() + txt.substr(1).toLowerCase())
}
