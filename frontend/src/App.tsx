import { Routes, Route, Navigate } from 'react-router-dom'
import Navigation from './components/Navigation'
import RequireAuth from './components/RequireAuth'
import { AuthProvider } from './contexts/AuthContext'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import PhysicianStatistics from './pages/PhysicianStatistics'
import ReportingDrCm2 from './pages/ReportingDrCm2'
import TopReferringDoctorsCm2 from './pages/TopReferringDoctorsCm2'
import TopReferringPracticesCm2 from './pages/TopReferringPracticesCm2'
import ReferrersGrowthCm2 from './pages/ReferrersGrowthCm2'
import ReferrersDeclineCm2 from './pages/ReferrersDeclineCm2'
import ReferrerHistoryCm2 from './pages/ReferrerHistoryCm2'
import DrSummaryCm2 from './pages/DrSummaryCm2'
import JamesReportCm2 from './pages/JamesReportCm2'
import TeeIoeProgressReport from './pages/TeeIoeProgressReport'
import ClinicalHome from './pages/ClinicalHome'
import AdminHome from './pages/AdminHome'
import AdminUsers from './pages/AdminUsers'
import AdminDatabase from './pages/AdminDatabase'
import AdminVersionHistory from './pages/AdminVersionHistory'
import SystemHealth from './pages/SystemHealth'
import ResearchHome from './pages/ResearchHome'
import ResearchStudyEditor from './pages/ResearchStudyEditor'
import WorkllistsHome from './pages/WorkllistsHome'
import SandBoxHome from './pages/SandBoxHome'

function App() {
  return (
    <AuthProvider>
      <div className="min-h-screen bg-background">
        <Navigation />
        <div className="container mx-auto py-6 px-4">
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route element={<RequireAuth />}>
              <Route path="/" element={<Navigate to="/dashboard" replace />} />
              <Route path="/dashboard" element={<Dashboard />} />
              <Route path="/clinical" element={<ClinicalHome />} />
              <Route path="/clinical/physician-statistics" element={<PhysicianStatistics />} />
              <Route path="/clinical/reporting-dr-cm2" element={<ReportingDrCm2 />} />
              <Route path="/clinical/dr-summary-cm2" element={<DrSummaryCm2 />} />
              <Route path="/clinical/james-report" element={<JamesReportCm2 />} />
              <Route path="/clinical/tee-ioe-progress" element={<TeeIoeProgressReport />} />
              <Route path="/clinical/top-referring-doctors-cm2" element={<TopReferringDoctorsCm2 />} />
              <Route path="/clinical/top-referring-practices-cm2" element={<TopReferringPracticesCm2 />} />
              <Route path="/clinical/referrers-growth-cm2" element={<ReferrersGrowthCm2 />} />
              <Route path="/clinical/referrers-decline-cm2" element={<ReferrersDeclineCm2 />} />
              <Route path="/clinical/referrer-history-cm2" element={<ReferrerHistoryCm2 />} />
              <Route path="/research" element={<ResearchHome />} />
              <Route path="/research/studies/new" element={<ResearchStudyEditor />} />
              <Route path="/research/studies/:id" element={<ResearchStudyEditor />} />
              <Route path="/workllists" element={<WorkllistsHome />} />
              <Route path="/sandbox" element={<SandBoxHome />} />
              <Route path="/admin" element={<AdminHome />} />
              <Route path="/admin/users" element={<AdminUsers />} />
              <Route path="/admin/database" element={<AdminDatabase />} />
              <Route path="/admin/health" element={<SystemHealth />} />
              <Route path="/admin/version-history" element={<AdminVersionHistory />} />
            </Route>
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </div>
      </div>
    </AuthProvider>
  )
}

export default App
