import { Stethoscope } from 'lucide-react'
import ReportingDrReport from '../components/ReportingDrReport'

export default function PhysicianStatistics() {
  return (
    <ReportingDrReport
      title="Reporting Dr - Syngo"
      subtitle="Reporting activity by physician and study type (H1PACS / Syngo)"
      icon={Stethoscope}
      physiciansUrl="/api/clinical/physicians"
      reportUrl="/api/clinical/physician-statistics"
      csvPrefix="reporting-dr-syngo"
    />
  )
}
