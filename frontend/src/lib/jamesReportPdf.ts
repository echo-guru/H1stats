import { jsPDF } from 'jspdf'

export interface JamesPdfQuarter {
  year: number
  quarter: number
  label: string
  echo: number
  seDse: number
  consults: number
}

export interface JamesPdfYear {
  year: number
  partial: boolean
  quarters: JamesPdfQuarter[]
  echoTotal: number
  seDseTotal: number
  consultsTotal: number
}

export interface JamesPdfSite {
  siteId: string
  siteName: string
  years: JamesPdfYear[]
}

export interface JamesPdfReport {
  dateFrom: string
  dateToExclusive: string
  currentYear: number
  sites: JamesPdfSite[]
}

const SERIES = [
  { key: 'echo' as const, label: 'Echo', rgb: [51, 117, 143] as const },
  { key: 'seDse' as const, label: 'SE / DSE', rgb: [105, 168, 188] as const },
  { key: 'consults' as const, label: 'Consults', rgb: [196, 163, 90] as const },
]

const BRAND: [number, number, number] = [51, 117, 143]
const ACCENT: [number, number, number] = [31, 79, 97]
const MUTED: [number, number, number] = [107, 114, 128]

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

function drawLegend(doc: jsPDF, x: number, y: number) {
  let cx = x
  doc.setFontSize(9)
  for (const s of SERIES) {
    setFill(doc, s.rgb)
    doc.roundedRect(cx, y - 2.5, 4, 4, 0.5, 0.5, 'F')
    doc.setTextColor(55, 65, 81)
    doc.text(s.label, cx + 6, y + 1)
    cx += doc.getTextWidth(s.label) + 14
  }
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
  const groupW = Math.min(slot * 0.72, 28)
  const barW = (groupW - 2) / series.length

  labels.forEach((label, i) => {
    const groupX = x + slot * i + (slot - groupW) / 2
    series.forEach((s, si) => {
      const v = s.values[i] ?? 0
      const h = maxY > 0 ? (v / maxY) * height : 0
      const bx = groupX + si * barW
      const by = y + height - h
      setFill(doc, s.rgb)
      doc.roundedRect(bx, by, Math.max(barW - 0.6, 0.8), Math.max(h, v > 0 ? 0.8 : 0), 0.3, 0.3, 'F')
    })
    const cx = x + slot * i + slot / 2
    doc.setFontSize(8)
    doc.setTextColor(55, 65, 81)
    doc.text(label.top, cx, y + height + 5, { align: 'center' })
    setText(doc, MUTED)
    doc.text(label.bottom, cx, y + height + 9, { align: 'center' })
  })
}

function drawTablesPage(doc: jsPDF, report: JamesPdfReport) {
  const pageW = doc.internal.pageSize.getWidth()
  const margin = 12
  const gap = 6
  const colW = (pageW - margin * 2 - gap * 2) / 3

  doc.setFont('helvetica', 'bold')
  doc.setFontSize(18)
  setText(doc, ACCENT)
  doc.text('James Report', margin, 16)

  doc.setFont('helvetica', 'normal')
  doc.setFontSize(9)
  setText(doc, MUTED)
  doc.text(
    `Greenslopes · Cleveland · Toowoomba  ·  ${report.dateFrom.slice(0, 10)} → ${report.dateToExclusive.slice(0, 10)} (end exclusive)`,
    margin,
    22,
  )
  doc.text('Echo / SE·DSE from completed CM2 studies; consults from diary appointments by campus.', margin, 27)

  drawLegend(doc, margin, 34)

  report.sites.forEach((site, si) => {
    const x0 = margin + si * (colW + gap)
    let y = 42

    doc.setFillColor(232, 241, 245)
    doc.roundedRect(x0, y - 5, colW, 8, 1, 1, 'F')
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(11)
    setText(doc, BRAND)
    doc.text(site.siteName, x0 + colW / 2, y, { align: 'center' })
    y += 8

    const cols = [
      { key: 'period', w: colW * 0.34, align: 'left' as const },
      { key: 'echo', w: colW * 0.22, align: 'right' as const },
      { key: 'se', w: colW * 0.22, align: 'right' as const },
      { key: 'cons', w: colW * 0.22, align: 'right' as const },
    ]

    const drawHeader = () => {
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(7)
      setText(doc, MUTED)
      let cx = x0
      ;['Period', 'Echo', 'SE/DSE', 'Consults'].forEach((h, i) => {
        const c = cols[i]!
        doc.text(h, c.align === 'right' ? cx + c.w - 1 : cx + 1, y, {
          align: c.align === 'right' ? 'right' : 'left',
        })
        cx += c.w
      })
      y += 3
      doc.setDrawColor(168, 201, 217)
      doc.setLineWidth(0.3)
      doc.line(x0, y, x0 + colW, y)
      y += 4
    }

    drawHeader()

    for (const year of site.years) {
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(8)
      setText(doc, ACCENT)
      doc.text(`${year.year}${year.partial ? ' · YTD' : ''}`, x0 + 1, y)
      y += 4.2

      for (const q of year.quarters) {
        doc.setFont('helvetica', 'normal')
        doc.setFontSize(8)
        doc.setTextColor(55, 65, 81)
        const values = [q.label, fmt(q.echo), fmt(q.seDse), fmt(q.consults)]
        let cx = x0
        values.forEach((v, i) => {
          const c = cols[i]!
          doc.text(v, c.align === 'right' ? cx + c.w - 1 : cx + 1, y, {
            align: c.align === 'right' ? 'right' : 'left',
          })
          cx += c.w
        })
        y += 3.8
      }

      doc.setFillColor(232, 241, 245)
      doc.rect(x0, y - 2.8, colW, 4.5, 'F')
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(8)
      setText(doc, ACCENT)
      const totals = [
        `${year.year} Total${year.partial ? ' (YTD)' : ''}`,
        fmt(year.echoTotal),
        fmt(year.seDseTotal),
        fmt(year.consultsTotal),
      ]
      let cx = x0
      totals.forEach((v, i) => {
        const c = cols[i]!
        doc.text(v, c.align === 'right' ? cx + c.w - 1 : cx + 1, y, {
          align: c.align === 'right' ? 'right' : 'left',
        })
        cx += c.w
      })
      y += 6
    }
  })
}

function drawSiteChartPage(doc: jsPDF, site: JamesPdfSite) {
  const pageW = doc.internal.pageSize.getWidth()
  const margin = 16

  doc.setFont('helvetica', 'bold')
  doc.setFontSize(16)
  setText(doc, ACCENT)
  doc.text(`${site.siteName} — quarterly trends`, margin, 18)

  doc.setFont('helvetica', 'normal')
  doc.setFontSize(9)
  setText(doc, MUTED)
  doc.text('Echo, SE/DSE and consults by quarter', margin, 24)
  drawLegend(doc, margin, 32)

  const points = site.years.flatMap((y) =>
    y.quarters.map((q) => ({
      top: q.label,
      bottom: String(y.year),
      echo: q.echo,
      seDse: q.seDse,
      consults: q.consults,
    })),
  )

  drawGroupedBars(doc, {
    x: margin + 10,
    y: 42,
    width: pageW - margin * 2 - 14,
    height: 120,
    labels: points.map((p) => ({ top: p.top, bottom: p.bottom })),
    series: SERIES.map((s) => ({
      label: s.label,
      rgb: s.rgb,
      values: points.map((p) => p[s.key]),
    })),
  })
}

function drawComparePage(doc: jsPDF, report: JamesPdfReport, year: number) {
  const pageW = doc.internal.pageSize.getWidth()
  const margin = 16
  const partial = year === report.currentYear

  doc.setFont('helvetica', 'bold')
  doc.setFontSize(16)
  setText(doc, ACCENT)
  doc.text(`Site comparison — ${year}${partial ? ' (YTD)' : ''}`, margin, 18)

  doc.setFont('helvetica', 'normal')
  doc.setFontSize(9)
  setText(doc, MUTED)
  doc.text('Year totals by campus', margin, 24)
  drawLegend(doc, margin, 32)

  const sites = report.sites.map((site) => {
    const block = site.years.find((y) => y.year === year)
    return {
      name: site.siteName,
      echo: block?.echoTotal ?? 0,
      seDse: block?.seDseTotal ?? 0,
      consults: block?.consultsTotal ?? 0,
    }
  })

  drawGroupedBars(doc, {
    x: margin + 10,
    y: 48,
    width: pageW - margin * 2 - 14,
    height: 110,
    labels: sites.map((s) => ({ top: s.name, bottom: '' })),
    series: SERIES.map((s) => ({
      label: s.label,
      rgb: s.rgb,
      values: sites.map((site) => site[s.key]),
    })),
  })
}

/** Build and download a multi-page James Report PDF (tables page, then chart pages). */
export function downloadJamesReportPdf(report: JamesPdfReport, compareYear?: number) {
  const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' })
  const year = compareYear ?? report.currentYear
  const chartPages = report.sites.length + 1 // one per site + comparison
  const totalPages = 1 + chartPages

  drawTablesPage(doc, report)

  report.sites.forEach((site) => {
    doc.addPage()
    drawSiteChartPage(doc, site)
  })

  doc.addPage()
  drawComparePage(doc, report, year)

  for (let i = 1; i <= totalPages; i++) {
    doc.setPage(i)
    pageFooter(doc, i, totalPages)
  }

  doc.save(`james-report-${report.currentYear}.pdf`)
}
