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
      collapsibleGroups
      resultsNotice="Imaging work only: Transthoracic Echo, Exercise Stress Echo, Dobutamine Stress Echo, and Transoesophageal Echo. ECG, Holter, event monitor, and blood pressure studies are excluded because reporting attribution for those modalities is not reliable in this data."
    />
  )
}
