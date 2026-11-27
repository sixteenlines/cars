import { Car, FileUp, Fuel, Plus, Settings, Users } from 'lucide-react'
import { Link } from 'react-router-dom'
import { photoUrl } from '../api'
import { Empty, Spinner, TopBar } from '../components/ui'
import { fmtKm, fmtMoneyShort } from '../format'
import { useMe, useVehicles } from '../hooks'
import { plural, t } from '../i18n'
import { InboxRow, useInbox } from './ImportUpload'

export default function Garage() {
  const { data: me } = useMe()
  const { data: vehicles, isLoading } = useVehicles()
  const { data: inbox = [] } = useInbox()

  return (
    <>
      <TopBar title="Car Tracker">
        {me?.isAdmin && <Link to="/admin" className="icon-btn" aria-label={t('Users & invites', 'Nutzer & Einladungen')}><Users size={20} /></Link>}
        <Link to="/settings" className="icon-btn" aria-label={t('Settings', 'Einstellungen')}><Settings size={20} /></Link>
      </TopBar>

      <div className="page-head">
        <div>
          <p className="faint" style={{ margin: 0 }}>{t('Hi', 'Hallo')} {me?.displayName}</p>
          <h1>{t('Your cars', 'Deine Autos')}</h1>
        </div>
        {!!vehicles?.length && <Link to="/import" className="btn"><FileUp size={18} /> {t('Import bill', 'Rechnung importieren')}</Link>}
      </div>

      {inbox.length > 0 && (
        <div className="section" style={{ marginTop: 0, marginBottom: 20 }}>
          <h2>{plural(inbox.length, ['bill', 'Rechnung'], ['bills', 'Rechnungen'])} {t('to review', 'zu prüfen')}</h2>
          <div className="card list">
            {inbox.slice(0, 3).map(d => <InboxRow key={d.id} doc={d} vehicleName={vehicles?.find(v => v.id === d.vehicleId)?.name} />)}
          </div>
          {inbox.length > 3 && <Link to="/import" className="faint">{t('Show all', 'Alle anzeigen')}</Link>}
        </div>
      )}

      {isLoading && <div className="center"><Spinner /></div>}
      {vehicles?.length === 0 && (
        <div className="card">
          <Empty icon={<Car size={40} />} title={t('No cars yet', 'Noch keine Autos')}>
            <p>{t('Add your first car to start tracking mods and costs.', 'Leg dein erstes Auto an, um Mods und Kosten festzuhalten.')}</p>
            <Link to="/vehicles/new" className="btn btn-primary"><Plus size={18} /> {t('Add car', 'Auto hinzufügen')}</Link>
            <p style={{ marginTop: 14 }}><Link to="/import/tmm" className="link">{t('Coming from Track My Mods? Import your CSV exports', 'Du kommst von Track My Mods? CSV-Exporte importieren')}</Link></p>
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
                  <div className="faint">{plural(v.stats.modCount, ['mod', 'Mod'], ['mods', 'Mods'])}</div>
                </div>
              </div>
              {v.stats.latestOdometer && <div className="faint" style={{ marginTop: 6 }}>{fmtKm(v.stats.latestOdometer)}</div>}
            </div>
          </Link>
          <Link to={`/vehicles/${v.id}/fuel/new`} className="quick-fuel" aria-label={t(`Add fill-up for ${v.name}`, `Tanken für ${v.name} eintragen`)}><Fuel size={18} /> {t('Fuel', 'Tanken')}</Link>
          </div>
        ))}
      </div>

      {!!vehicles?.length && (
        <Link to="/vehicles/new" className="fab"><Plus size={22} /> {t('Car', 'Auto')}</Link>
      )}
    </>
  )
}
