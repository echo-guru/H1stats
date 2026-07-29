import { Routes, Route, Navigate } from 'react-router-dom'
import Navigation from './components/Navigation'
import RequireAuth from './components/RequireAuth'
import { AuthProvider } from './contexts/AuthContext'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import PhysicianStatistics from './pages/PhysicianStatistics'
import ReportingDrCm2 from './pages/ReportingDrCm2'
import TopReferringDoctorsCm2 from './pages/TopReferringDoctorsCm2'
import ClinicalHome from './pages/ClinicalHome'
import AdminHome from './pages/AdminHome'
import AdminUsers from './pages/AdminUsers'
import AdminDatabase from './pages/AdminDatabase'
import SystemHealth from './pages/SystemHealth'

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
              <Route path="/clinical/top-referring-doctors-cm2" element={<TopReferringDoctorsCm2 />} />
              <Route path="/admin" element={<AdminHome />} />
              <Route path="/admin/users" element={<AdminUsers />} />
              <Route path="/admin/database" element={<AdminDatabase />} />
              <Route path="/admin/health" element={<SystemHealth />} />
            </Route>
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </div>
      </div>
    </AuthProvider>
  )
}

export default App
