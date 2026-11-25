import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Car, FileUp, Fuel, LogOut, Plus, Users } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { api, photoUrl } from '../api'
import { Empty, Spinner, TopBar } from '../components/ui'
import { fmtKm, fmtMoneyShort } from '../format'
import { useMe, useVehicles } from '../hooks'
import { InboxRow, useInbox } from './ImportUpload'

export default function Garage() {
  const { data: me } = useMe()
  const { data: vehicles, isLoading } = useVehicles()
  const { data: inbox = [] } = useInbox()
  const qc = useQueryClient()
  const navigate = useNavigate()

  const logout = useMutation({
    mutationFn: () => api.post('/auth/logout'),
    onSuccess: () => { qc.clear(); navigate('/login') },
  })

  return (
    <>
      <TopBar title="Car Tracker">
        {me?.isAdmin && <Link to="/admin" className="icon-btn" aria-label="Users & invites"><Users size={20} /></Link>}
        <button className="icon-btn" aria-label="Sign out" onClick={() => logout.mutate()}><LogOut size={20} /></button>
      </TopBar>

      <div className="page-head">
        <div>
          <p className="faint" style={{ margin: 0 }}>Hi {me?.displayName}</p>
          <h1>Your cars</h1>
        </div>
        {!!vehicles?.length && <Link to="/import" className="btn"><FileUp size={18} /> Import bill</Link>}
      </div>

      {inbox.length > 0 && (
        <div className="section" style={{ marginTop: 0, marginBottom: 20 }}>
          <h2>{inbox.length} bill{inbox.length === 1 ? '' : 's'} to review</h2>
          <div className="card list">
            {inbox.slice(0, 3).map(d => <InboxRow key={d.id} doc={d} vehicleName={vehicles?.find(v => v.id === d.vehicleId)?.name} />)}
          </div>
          {inbox.length > 3 && <Link to="/import" className="faint">Show all</Link>}
        </div>
      )}

      {isLoading && <div className="center"><Spinner /></div>}
      {vehicles?.length === 0 && (
        <div className="card">
          <Empty icon={<Car size={40} />} title="No cars yet">
            <p>Add your first car to start tracking mods and costs.</p>
            <Link to="/vehicles/new" className="btn btn-primary"><Plus size={18} /> Add car</Link>
          </Empty>
        </div>
      )}

      <div className="vehicle-grid">
        {vehicles?.map(v => (
          <div key={v.id} className="vehicle-card-wrap">
          <Link to={`/vehicles/${v.id}`} className="card vehicle-card">
            <div className="cover">
              {v.coverPhotoId ? <img src={photoUrl(v.coverPhotoId, 'full')} alt="" loading="lazy" /> : <Car size={48} />}
            </div>
            <div className="body">
              <div className="row">
                <div className="grow">
                  <div className="name">{v.name}</div>
                  <div className="faint">{[v.year, v.make, v.model, v.badge].filter(Boolean).join(' ')}</div>
                </div>
                <div style={{ textAlign: 'right' }}>
                  <div style={{ fontWeight: 750 }}>{fmtMoneyShort(v.stats.totalSpent)}</div>
                  <div className="faint">{v.stats.modCount} {v.stats.modCount === 1 ? 'mod' : 'mods'}</div>
                </div>
              </div>
              {v.stats.latestOdometer && <div className="faint" style={{ marginTop: 6 }}>{fmtKm(v.stats.latestOdometer)}</div>}
            </div>
          </Link>
          <Link to={`/vehicles/${v.id}/fuel/new`} className="quick-fuel" aria-label={`Add fill-up for ${v.name}`}><Fuel size={18} /> Fuel</Link>
          </div>
        ))}
      </div>

      {!!vehicles?.length && (
        <Link to="/vehicles/new" className="fab"><Plus size={22} /> Car</Link>
      )}
    </>
  )
}
