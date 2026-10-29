import { jsPDF } from 'jspdf'

export interface JamesPdfQuarter {
  year: number
  quarter: number
  label: string
  counts: number[]
}

export interface JamesPdfYear {
  year: number
  partial: boolean
  quarters: JamesPdfQuarter[]
  totals: number[]
}

export interface JamesPdfSite {
  siteId: string
  siteName: string
  years: JamesPdfYear[]
}

export interface JamesPdfTest {
  code: string
  displayName: string
  shortName: string
}

export interface JamesPdfReport {
  dateFrom: string
  dateTo: string
  tests: JamesPdfTest[]
  sites: JamesPdfSite[]
}

const PALETTE: ReadonlyArray<readonly [number, number, number]> = [
  [51, 117, 143],
  [105, 168, 188],
  [196, 163, 90],
  [196, 123, 107],
  [91, 138, 114],
  [107, 91, 149],
  [212, 160, 23],
  [74, 111, 165],
  [163, 93, 106],
  [122, 139, 74],
  [139, 94, 60],
  [61, 122, 106],
  [92, 107, 115],
]

const BRAND: [number, number, number] = [51, 117, 143]
const ACCENT: [number, number, number] = [31, 79, 97]
const MUTED: [number, number, number] = [107, 114, 128]

interface PdfSeries {
  label: string
  rgb: readonly [number, number, number]
}

function seriesFor(tests: JamesPdfTest[]): PdfSeries[] {
  return tests.map((t, i) => ({
    label: t.shortName,
    rgb: PALETTE[i % PALETTE.length]!,
  }))
}

function setText(doc: jsPDF, rgb: [number, number, number]) {
  doc.setTextColor(rgb[0], rgb[1], rgb[2])
}

function setFill(doc: jsPDF, rgb: readonly [number, number, number]) {
  doc.setFillColor(rgb[0], rgb[1], rgb[2])
}

function fmt(n: number): string {
  return n.toLocaleString()
}

function niceMax(value: number): number {
  if (value <= 0) return 10
  const exp = Math.floor(Math.log10(value))
  const mag = 10 ** exp
  const n = value / mag
  const nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10
  return nice * mag
}

function pageFooter(doc: jsPDF, page: number, total: number) {
  const w = doc.internal.pageSize.getWidth()
  const h = doc.internal.pageSize.getHeight()
  doc.setFontSize(8)
  setText(doc, MUTED)
  doc.text('James Report · Hearts 1st', 14, h - 8)
  doc.text(`Page ${page} of ${total}`, w - 14, h - 8, { align: 'right' })
}

function drawLegend(doc: jsPDF, x: number, y: number, maxX: number, series: PdfSeries[]): number {
  let cx = x
  let cy = y
  doc.setFontSize(9)
  for (const s of series) {
    const w = doc.getTextWidth(s.label) + 14
    if (cx > x && cx + w > maxX) {
      cx = x
      cy += 6
    }
    setFill(doc, s.rgb)
    doc.roundedRect(cx, cy - 2.5, 4, 4, 0.5, 0.5, 'F')
    doc.setTextColor(55, 65, 81)
    doc.text(s.label, cx + 6, cy + 1)
    cx += w
  }
  return cy
}

function drawGroupedBars(
  doc: jsPDF,
  opts: {
    x: number
    y: number
    width: number
    height: number
    labels: { top: string; bottom: string }[]
    series: { label: string; rgb: readonly [number, number, number]; values: number[] }[]
  },
) {
  const { x, y, width, height, labels, series } = opts
  const maxY = niceMax(Math.max(0, ...series.flatMap((s) => s.values)))
  const tickCount = 4

  doc.setDrawColor(168, 201, 217)
  doc.setLineWidth(0.3)
  for (let i = 0; i <= tickCount; i++) {
    const v = (maxY / tickCount) * i
    const gy = y + height - (v / maxY) * height
    doc.setDrawColor(i === 0 ? 168 : 232, i === 0 ? 201 : 241, i === 0 ? 217 : 245)
    doc.line(x, gy, x + width, gy)
    doc.setFontSize(8)
    setText(doc, MUTED)
    doc.text(fmt(Math.round(v)), x - 2, gy + 1, { align: 'right' })
  }

  const n = labels.length || 1
  const slot = width / n
  const groupW = Math.min(slot * 0.72, Math.max(18, series.length * 8))
  const barW = series.length > 0 ? (groupW - 1) / series.length : groupW

  labels.forEach((label, i) => {
    const groupX = x + slot * i + (slot - groupW) / 2
    series.forEach((s, si) => {
      const v = s.values[i] ?? 0
      const h = maxY > 0 ? (v / maxY) * height : 0
      const bx = groupX + si * barW
      const by = y + height - h
      setFill(doc, s.rgb)
      doc.roundedRect(bx, by, Math.max(barW - 0.4, 0.6), Math.max(h, v > 0 ? 0.6 : 0), 0.3, 0.3, 'F')
    })
    const cx = x + slot * i + slot / 2
    doc.setFontSize(8)
    doc.setTextColor(55, 65, 81)
    doc.text(label.top, cx, y + height + 5, { align: 'center' })
    setText(doc, MUTED)
    if (label.bottom) doc.text(label.bottom, cx, y + height + 9, { align: 'center' })
  })
}

function columnWidths(total: number, testCount: number): number[] {
  const period = Math.min(42, Math.max(24, total * 0.22))
  const rest = Math.max(total - period, 10)
  const each = testCount > 0 ? rest / testCount : rest
  return [period, ...Array.from({ length: testCount }, () => each)]
}

function drawBanner(doc: jsPDF, report: JamesPdfReport, margin: number): number {
  const pageW = doc.internal.pageSize.getWidth()
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(16)
  setText(doc, ACCENT)
  doc.text('James Report', margin, 14)

  doc.setFont('helvetica', 'normal')
  doc.setFontSize(9)
  setText(doc, MUTED)
  const tests = report.tests.map((t) => t.displayName).join(', ') || 'No tests'
  const range = `${report.dateFrom.slice(0, 10)} → ${report.dateTo.slice(0, 10)}`
  doc.text(`Greenslopes · Cleveland · Toowoomba  ·  ${range}`, margin, 20)
  const lines = doc.splitTextToSize(`Tests: ${tests}`, pageW - margin * 2)
  doc.text(lines, margin, 25)
  return 25 + lines.length * 4 + 2
}

function drawColumnHeader(
  doc: jsPDF,
  x: number,
  y: number,
  widths: number[],
  tests: JamesPdfTest[],
): number {
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(tests.length > 8 ? 7 : 8)
  setText(doc, MUTED)
  const headers = ['Period', ...tests.map((t) => t.shortName)]
  let cx = x
  headers.forEach((h, i) => {
    const w = widths[i] ?? 0
    const align = i === 0 ? 'left' : 'right'
    doc.text(h, align === 'right' ? cx + w - 1 : cx + 1, y, { align })
    cx += w
  })
  y += 2
  doc.setDrawColor(168, 201, 217)
  doc.setLineWidth(0.3)
  const width = widths.reduce((sum, w) => sum + w, 0)
  doc.line(x, y, x + width, y)
  return y + 4
}

function drawRow(
  doc: jsPDF,
  x: number,
  y: number,
  widths: number[],
  values: string[],
  bold: boolean,
) {
  doc.setFont('helvetica', bold ? 'bold' : 'normal')
  doc.setFontSize(values.length > 9 ? 7 : 8)
  if (bold) setText(doc, ACCENT)
  else doc.setTextColor(55, 65, 81)
  let cx = x
  values.forEach((v, i) => {
    const w = widths[i] ?? 0
    const align = i === 0 ? 'left' : 'right'
    doc.text(v, align === 'right' ? cx + w - 1 : cx + 1, y, { align })
    cx += w
  })
}

function drawTables(doc: jsPDF, report: JamesPdfReport) {
  const pageW = doc.internal.pageSize.getWidth()
  const pageH = doc.internal.pageSize.getHeight()
  const margin = 12
  const bottom = 14
  const width = pageW - margin * 2
  const widths = columnWidths(width, report.tests.length)

  let y = drawBanner(doc, report, margin)

  const newPage = () => {
    doc.addPage()
    y = drawBanner(doc, report, margin)
  }

  const ensure = (needed: number) => {
    if (y + needed > pageH - bottom) newPage()
  }

  for (const site of report.sites) {
    ensure(22)
    doc.setFillColor(232, 241, 245)
    doc.roundedRect(margin, y - 4, width, 8, 1, 1, 'F')
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(11)
    setText(doc, BRAND)
    doc.text(site.siteName, margin + 2, y + 1)
    y += 8
    y = drawColumnHeader(doc, margin, y, widths, report.tests)

    for (const year of site.years) {
      ensure(8)
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(8)
      setText(doc, ACCENT)
      doc.text(`${year.year}${year.partial ? ' · partial' : ''}`, margin + 1, y)
      y += 4.2

      for (const q of year.quarters) {
        ensure(5)
        drawRow(doc, margin, y, widths, [q.label, ...report.tests.map((_, i) => fmt(q.counts[i] ?? 0))], false)
        y += 3.8
      }

      ensure(7)
      doc.setFillColor(232, 241, 245)
      doc.rect(margin, y - 2.8, width, 4.5, 'F')
      const totalLabel = `${year.year} Total${year.partial ? ' (partial)' : ''}`
      drawRow(
        doc,
        margin,
        y,
        widths,
        [totalLabel, ...report.tests.map((_, i) => fmt(year.totals[i] ?? 0))],
        true,
      )
      y += 6
    }

    y += 4
  }
}

function drawSiteChartPage(doc: jsPDF, site: JamesPdfSite, tests: JamesPdfTest[]) {
  const pageW = doc.internal.pageSize.getWidth()
  const margin = 16
  const series = seriesFor(tests)

  doc.setFont('helvetica', 'bold')
  doc.setFontSize(16)
  setText(doc, ACCENT)
  doc.text(`${site.siteName} — quarterly trends`, margin, 16)

  doc.setFont('helvetica', 'normal')
  doc.setFontSize(9)
  setText(doc, MUTED)
  doc.text('Site volumes by quarter', margin, 22)
  const legendY = drawLegend(doc, margin, 30, pageW - margin, series)

  const points = site.years.flatMap((y) =>
    y.quarters.map((q) => ({
      top: q.label,
      bottom: String(y.year),
      counts: q.counts,
    })),
  )

  drawGroupedBars(doc, {
    x: margin + 10,
    y: legendY + 8,
    width: pageW - margin * 2 - 14,
    height: 110,
    labels: points.map((p) => ({ top: p.top, bottom: p.bottom })),
    series: series.map((s, i) => ({
      label: s.label,
      rgb: s.rgb,
      values: points.map((p) => p.counts[i] ?? 0),
    })),
  })
}

function drawComparePage(doc: jsPDF, report: JamesPdfReport, year: number) {
  const pageW = doc.internal.pageSize.getWidth()
  const margin = 16
  const series = seriesFor(report.tests)
  const partial = report.sites.some((site) => site.years.some((y) => y.year === year && y.partial))

  doc.setFont('helvetica', 'bold')
  doc.setFontSize(16)
  setText(doc, ACCENT)
  doc.text(`Site comparison — ${year}${partial ? ' (partial)' : ''}`, margin, 16)

  doc.setFont('helvetica', 'normal')
  doc.setFontSize(9)
  setText(doc, MUTED)
  doc.text('Year totals by campus', margin, 22)
  const legendY = drawLegend(doc, margin, 30, pageW - margin, series)

  const sites = report.sites.map((site) => {
    const block = site.years.find((y) => y.year === year)
    return {
      name: site.siteName,
      totals: block?.totals ?? [],
    }
  })

  drawGroupedBars(doc, {
    x: margin + 10,
    y: legendY + 10,
    width: pageW - margin * 2 - 14,
    height: 110,
    labels: sites.map((s) => ({ top: s.name, bottom: '' })),
    series: series.map((s, i) => ({
      label: s.label,
      rgb: s.rgb,
      values: sites.map((site) => site.totals[i] ?? 0),
    })),
  })
}

/** Build and download a multi-page James Report PDF (tables, then chart pages). */
export function downloadJamesReportPdf(report: JamesPdfReport, compareYear?: number) {
  const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' })
  const years = report.sites[0]?.years.map((y) => y.year) ?? []
  const year = compareYear ?? years[years.length - 1]

  drawTables(doc, report)

  report.sites.forEach((site) => {
    doc.addPage()
    drawSiteChartPage(doc, site, report.tests)
  })

  if (year != null) {
    doc.addPage()
    drawComparePage(doc, report, year)
  }

  const total = doc.getNumberOfPages()
  for (let i = 1; i <= total; i++) {
    doc.setPage(i)
    pageFooter(doc, i, total)
  }

  const from = report.dateFrom.slice(0, 10)
  const to = report.dateTo.slice(0, 10)
  doc.save(`james-report-${from}-${to}.pdf`)
}
