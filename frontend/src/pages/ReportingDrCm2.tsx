import { Stethoscope } from 'lucide-react'
import ReportingDrReport from '../components/ReportingDrReport'

export default function ReportingDrCm2() {
  return (
    <ReportingDrReport
      title="Reporting Dr - CM2"
      subtitle="Reporting activity by cardiologist and investigation type (Oracle CM2 / Hearts1st)"
      icon={Stethoscope}
      physiciansUrl="/api/clinical/cm2/physicians"
      reportUrl="/api/clinical/cm2/physician-statistics"
      csvPrefix="reporting-dr-cm2"
      physicianOptions
      formatPhysicianName={(name) => name}
      studyTypeLabel="Investigation Type"
      physicianFilterLabel="Reporting Cardiologist"
    />
  )
}
