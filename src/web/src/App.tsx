import { useQueryClient } from '@tanstack/react-query'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { keys } from './hooks'
import { LangProvider } from './i18n'
import { useMe } from './hooks'
import { Spinner } from './components/ui'
import Login from './pages/Login'
import Register from './pages/Register'
import Garage from './pages/Garage'
import VehicleForm from './pages/VehicleForm'
import VehicleDetail from './pages/VehicleDetail'
import EntryForm from './pages/EntryForm'
import EntryDetail from './pages/EntryDetail'
import Admin from './pages/Admin'
import ImportUpload from './pages/ImportUpload'
import ImportReview from './pages/ImportReview'
import FuelForm from './pages/FuelForm'
import Settings from './pages/Settings'
import TmmImport from './pages/TmmImport'

function RequireAuth({ children, admin }: { children: React.ReactNode; admin?: boolean }) {
  const { data: me, isLoading } = useMe()
  const location = useLocation()
  if (isLoading) return <div className="center"><Spinner /></div>
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  if (admin && !me.isAdmin) return <Navigate to="/" replace />
  return <>{children}</>
}

export default function App() {
  const { data: me } = useMe()
  const qc = useQueryClient()
  return (
    <LangProvider accountLang={me?.language}
      onLangChange={() => qc.invalidateQueries({ predicate: q => q.queryKey[0] !== keys.me[0] })}>
    <div className="app">
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/" element={<RequireAuth><Garage /></RequireAuth>} />
        <Route path="/vehicles/new" element={<RequireAuth><VehicleForm /></RequireAuth>} />
        <Route path="/vehicles/:id" element={<RequireAuth><VehicleDetail /></RequireAuth>} />
        <Route path="/vehicles/:id/edit" element={<RequireAuth><VehicleForm /></RequireAuth>} />
        <Route path="/vehicles/:id/entries/new" element={<RequireAuth><EntryForm /></RequireAuth>} />
        <Route path="/entries/:entryId" element={<RequireAuth><EntryDetail /></RequireAuth>} />
        <Route path="/entries/:entryId/edit" element={<RequireAuth><EntryForm /></RequireAuth>} />
        <Route path="/vehicles/:id/fuel/new" element={<RequireAuth><FuelForm /></RequireAuth>} />
        <Route path="/fuel/:fuelId/edit" element={<RequireAuth><FuelForm /></RequireAuth>} />
        <Route path="/import" element={<RequireAuth><ImportUpload /></RequireAuth>} />
        <Route path="/import/tmm" element={<RequireAuth><TmmImport /></RequireAuth>} />
        <Route path="/import/:docId" element={<RequireAuth><ImportReview /></RequireAuth>} />
        <Route path="/settings" element={<RequireAuth><Settings /></RequireAuth>} />
        <Route path="/admin" element={<RequireAuth admin><Admin /></RequireAuth>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </div>
    </LangProvider>
  )
}
