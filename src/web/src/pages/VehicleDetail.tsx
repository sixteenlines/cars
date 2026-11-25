import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Car, FileUp, Fuel, ListPlus, Pencil, Plus, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, FUEL_GRADES, gradeLabel, photoUrl, uploadPhotos, type FuelUp, type Photo, type Vehicle } from '../api'
import { EntryRow } from '../components/EntryRow'
import { PhotoGrid, type PendingPhoto } from '../components/PhotoGrid'
import { Empty, ErrorText, Spinner, TopBar } from '../components/ui'
import { fmtDate, fmtKm, fmtMoney, fmtMoneyShort, fmtNum } from '../format'
import { keys, useEntries, useFuel, usePhotos, useVehicle } from '../hooks'
import { plural, t } from '../i18n'

const FILTERS = [
  { key: 'all', label: () => t('All', 'Alle') },
  { key: 'Mod', label: () => 'Mods' },
  { key: 'Maintenance', label: () => t('Maintenance', 'Wartung') },
  { key: 'Expense', label: () => t('Expenses', 'Ausgaben') },
  { key: 'planned', label: () => t('Planned', 'Geplant') },
] as const
type FilterKey = (typeof FILTERS)[number]['key']

export default function VehicleDetail() {
  const vehicleId = Number(useParams().id)
  const { data: v, isLoading } = useVehicle(vehicleId)
  const [params, setParams] = useSearchParams()
  const tab = params.get('tab') === 'photos' ? 'photos' : params.get('tab') === 'fuel' ? 'fuel' : 'entries'

  if (isLoading) return <div className="center"><Spinner /></div>
  if (!v) return <><TopBar back="/" title={t('Not found', 'Nicht gefunden')} /><Empty title={t("This car doesn't exist (anymore).", 'Dieses Auto gibt es nicht (mehr).')} /></>

  return (
    <>
      <TopBar back="/" title={v.name}>
        <Link to={`/vehicles/${v.id}/fuel/new`} className="icon-btn" aria-label={t('Add fill-up', 'Tanken eintragen')}><Fuel size={20} /></Link>
        <Link to={`/import?vehicleId=${v.id}`} className="icon-btn" aria-label={t('Import bill', 'Rechnung importieren')}><FileUp size={20} /></Link>
        <Link to={`/vehicles/${v.id}/edit`} className="icon-btn" aria-label={t('Edit car', 'Auto bearbeiten')}><Pencil size={20} /></Link>
      </TopBar>

      <div className="hero">
        {v.coverPhotoId
          ? <img src={photoUrl(v.coverPhotoId, 'full')} alt="" />
          : <button className="placeholder btn-ghost" style={{ border: 0, cursor: 'pointer' }} onClick={() => setParams({ tab: 'photos' })}>
              <div><Car size={56} /><div className="faint">{t('Add a photo', 'Foto hinzufügen')}</div></div>
            </button>}
      </div>

      <div className="page-head">
        <div>
          <h1>{v.name}</h1>
          <div className="muted">{[v.year, v.make, v.model, v.badge].filter(Boolean).join(' ')}{v.plate ? ` · ${v.plate}` : ''}
            {v.stats.latestOdometer != null ? ` · ${fmtKm(v.stats.latestOdometer)}` : ''}</div>
        </div>
      </div>

      <Stats v={v} />

      <div className="tabs" role="tablist">
        <button role="tab" className={tab === 'entries' ? 'on' : ''} onClick={() => setParams({}, { replace: true })}>
          {t('Entries', 'Einträge')} ({v.stats.entryCount})
        </button>
        <button role="tab" className={tab === 'fuel' ? 'on' : ''} onClick={() => setParams({ tab: 'fuel' }, { replace: true })}>
          {t('Fuel', 'Tanken')} ({v.stats.fuelCount})
        </button>
        <button role="tab" className={tab === 'photos' ? 'on' : ''} onClick={() => setParams({ tab: 'photos' }, { replace: true })}>
          {t('Photos', 'Fotos')} ({v.stats.photoCount})
        </button>
      </div>

      {tab === 'entries' ? <EntriesTab vehicle={v} /> : tab === 'fuel' ? <FuelTab vehicle={v} /> : <PhotosTab vehicle={v} />}
    </>
  )
}

function Stats({ v }: { v: Vehicle }) {
  const s = v.stats
  return (
    <div className="stats">
      <div className="card stat"><div className="k">{t('Total spent', 'Gesamtkosten')}</div><div className="v">{fmtMoneyShort(s.totalSpent)}</div></div>
      <div className="card stat"><div className="k">Mods</div><div className="v">{fmtMoneyShort(s.modsTotal)}</div><div className="faint">{s.modCount} {t('done', 'erledigt')}</div></div>
      <div className="card stat"><div className="k">{t('Maintenance', 'Wartung')}</div><div className="v">{fmtMoneyShort(s.maintenanceTotal)}</div><div className="faint">{t('last', 'zuletzt')} {fmtDate(s.lastMaintenanceDate)}</div></div>
      {s.fuelCount > 0 && (
        <div className="card stat"><div className="k">{t('Fuel', 'Sprit')}</div><div className="v">{fmtMoneyShort(s.fuelTotal)}</div>
          <div className="faint">{s.avgConsumption != null ? `Ø ${fmtL100(s.avgConsumption)}` : plural(s.fuelCount, ['fill-up', 'Tankfüllung'], ['fill-ups', 'Tankfüllungen'])}</div></div>
      )}
      {s.plannedCount > 0 && (
        <div className="card stat"><div className="k">{t('Planned', 'Geplant')}</div><div className="v">{fmtMoneyShort(s.plannedTotal)}</div><div className="faint">{plural(s.plannedCount, ['item', 'Eintrag'], ['items', 'Einträge'])}</div></div>
      )}
    </div>
  )
}

const fmtL100 = (n: number) => `${fmtNum(n, 1)} L/100 km`
const fmtL = (n: number | null) => (n == null ? '–' : `${fmtNum(n, 2)} L`)
const fmtPpl = (n: number | null) => (n == null ? '' : `${fmtNum(n, 3)} €/L`)

function FuelTab({ vehicle }: { vehicle: Vehicle }) {
  const { data: log, isLoading } = useFuel(vehicle.id)
  if (isLoading || !log) return <div className="center"><Spinner /></div>
  const s = log.stats
  const ppl = FUEL_GRADES.filter(g => s.avgPricePerLiter[g.value] != null)

  return (
    <>
      {log.fuelUps.length === 0 ? (
        <div className="card">
          <Empty icon={<Fuel size={36} />} title={t('No fill-ups yet', 'Noch nicht getankt')}>
            <p>{t('Pick the grade, type the total and price per litre, or snap the pump display.', 'Sorte wählen, Betrag und Literpreis eintippen – oder einfach die Zapfsäule fotografieren.')}</p>
            <Link to={`/vehicles/${vehicle.id}/fuel/new`} className="btn btn-primary"><Plus size={18} /> {t('Add fill-up', 'Tanken eintragen')}</Link>
          </Empty>
        </div>
      ) : (
        <>
          <div className="stats" style={{ marginBottom: 12 }}>
            <div className="card stat"><div className="k">{t('Consumption', 'Verbrauch')}</div>
              <div className="v">{s.avgConsumption != null ? fmtL100(s.avgConsumption) : '–'}</div>
              <div className="faint">{s.distanceKm ? t(`over ${fmtKm(s.distanceKm)}`, `über ${fmtKm(s.distanceKm)}`) : t('needs 2 full tanks with km', 'braucht 2 volle Tankfüllungen mit km')}</div></div>
            <div className="card stat"><div className="k">{t('Cost / 100 km', 'Kosten / 100 km')}</div>
              <div className="v">{s.costPer100Km != null ? fmtMoney(s.costPer100Km) : '–'}</div></div>
            <div className="card stat"><div className="k">{t('This month', 'Diesen Monat')}</div><div className="v">{fmtMoneyShort(s.thisMonthCost)}</div>
              <div className="faint">{fmtMoneyShort(s.thisYearCost)} {t('this year', 'dieses Jahr')}</div></div>
            <div className="card stat"><div className="k">{t('Ø price / L', 'Ø Preis / L')}</div>
              {ppl.length === 0 ? <div className="v">–</div> : ppl.map(g => (
                <div key={g.value} className="ppl-row"><span className="faint">{g.label}</span><span>{fmtPpl(s.avgPricePerLiter[g.value])}</span></div>
              ))}</div>
          </div>
          <div className="card list">{log.fuelUps.map(f => <FuelRow key={f.id} f={f} />)}</div>
          <div className="totals faint"><span>{plural(s.count, ['fill-up', 'Tankfüllung'], ['fill-ups', 'Tankfüllungen'])} · {fmtL(s.totalLiters)}</span><span>{fmtMoneyShort(s.totalCost)}</span></div>
        </>
      )}
      <Link to={`/vehicles/${vehicle.id}/fuel/new`} className="fab"><Plus size={22} /> {t('Fuel', 'Tanken')}</Link>
    </>
  )
}

function FuelRow({ f }: { f: FuelUp }) {
  return (
    <Link to={`/fuel/${f.id}/edit`} className="list-item">
      <div className={`fuel-badge g${f.grade ?? 'x'}`} title={gradeLabel(f.grade)}>{f.grade === 'diesel' ? 'D' : f.grade ?? '?'}</div>
      <div className="main">
        <div className="t">{fmtL(f.liters)}{f.pricePerLiter != null && <span className="faint"> · {fmtPpl(f.pricePerLiter)}</span>}</div>
        <div className="s">
          {fmtDate(f.date)}{f.odometer != null && ` · ${fmtKm(f.odometer)}`}{f.station && ` · ${f.station}`}{!f.fullTank && ` · ${t('partial', 'Teilfüllung')}`}
        </div>
      </div>
      <div className="amount">
        <div>{fmtMoney(f.total)}</div>
        {f.consumption != null && (
          <div className={`faint ${f.consumptionPlausible ? '' : 'error'}`} title={t(`${fmtKm(f.distanceKm)} since last full tank`, `${fmtKm(f.distanceKm)} seit dem letzten Volltanken`)}>
            {fmtL100(f.consumption)}{f.consumptionPlausible ? '' : ' ?'}
          </div>
        )}
      </div>
    </Link>
  )
}

function EntriesTab({ vehicle }: { vehicle: Vehicle }) {
  const { data: entries, isLoading } = useEntries(vehicle.id)
  const [filter, setFilter] = useState<FilterKey>('all')
  const [tag, setTag] = useState<string | null>(null)
  const [q, setQ] = useState('')

  const tags = useMemo(() => {
    const counts = new Map<string, number>()
    entries?.forEach(e => e.tags.forEach(t => counts.set(t, (counts.get(t) ?? 0) + 1)))
    return [...counts.entries()].sort((a, b) => b[1] - a[1]).map(([t]) => t)
  }, [entries])

  const visible = useMemo(() => {
    const needle = q.trim().toLowerCase()
    return (entries ?? []).filter(e =>
      (filter === 'all' || (filter === 'planned' ? e.status === 'Planned' : e.kind === filter)) &&
      (!tag || e.tags.includes(tag)) &&
      (!needle || [e.title, e.vendor, e.notes, ...e.tags, ...e.lineItems.flatMap(l => [l.name, l.partNumber, l.brand])]
        .some(s => s?.toLowerCase().includes(needle))))
  }, [entries, filter, tag, q])

  const sum = visible.reduce((acc, e) => acc + e.total, 0)
  const newKind = filter === 'Maintenance' || filter === 'Expense' ? filter : 'Mod'

  return (
    <>
      <div className="stack" style={{ gap: 10, marginBottom: 12 }}>
        <label className="field search">
          <Search size={18} />
          <input type="search" placeholder={t('Search title, part number, shop…', 'Titel, Teilenummer, Werkstatt suchen…')} value={q} onChange={e => setQ(e.target.value)} />
        </label>
        <div className="chips">
          {FILTERS.map(f => (
            <button key={f.key} className={`chip ${filter === f.key ? 'active' : ''}`} onClick={() => setFilter(f.key)}>{f.label()}</button>
          ))}
        </div>
        {tags.length > 0 && (
          <div className="chips">
            {tags.map(t => (
              <button key={t} className={`chip ${tag === t ? 'active' : ''}`} onClick={() => setTag(tag === t ? null : t)}>#{t}</button>
            ))}
          </div>
        )}
      </div>

      {isLoading && <div className="center"><Spinner /></div>}
      {entries && (visible.length === 0 ? (
        <div className="card">
          <Empty icon={<ListPlus size={36} />} title={entries.length ? t('Nothing matches', 'Nichts gefunden') : t('No entries yet', 'Noch keine Einträge')}>
            {!entries.length && <>
              <p>{t('Add a mod or repair — or import a bill and let it fill in the parts.', 'Trag eine Mod oder Reparatur ein – oder importier eine Rechnung, dann werden die Teile automatisch ausgefüllt.')}</p>
              <Link to={`/import?vehicleId=${vehicle.id}`} className="btn btn-primary"><FileUp size={18} /> {t('Import bill', 'Rechnung importieren')}</Link>
            </>}
          </Empty>
        </div>
      ) : (
        <>
          <div className="card list">{visible.map(e => <EntryRow key={e.id} entry={e} />)}</div>
          <div className="totals faint"><span>{plural(visible.length, ['entry', 'Eintrag'], ['entries', 'Einträge'])}</span><span>{fmtMoneyShort(sum)}</span></div>
        </>
      ))}

      <Link to={`/vehicles/${vehicle.id}/entries/new?kind=${newKind}`} className="fab"><Plus size={22} /> {t('Entry', 'Eintrag')}</Link>
    </>
  )
}

function PhotosTab({ vehicle }: { vehicle: Vehicle }) {
  const { data: photos = [], isLoading } = usePhotos(vehicle.id)
  const qc = useQueryClient()
  const [pending, setPending] = useState<PendingPhoto[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = () => {
    qc.invalidateQueries({ queryKey: keys.photos(vehicle.id) })
    qc.invalidateQueries({ queryKey: keys.vehicle(vehicle.id) })
    qc.invalidateQueries({ queryKey: keys.vehicles })
    qc.invalidateQueries({ queryKey: keys.entries(vehicle.id) })
  }

  const upload = useMutation({
    mutationFn: (items: PendingPhoto[]) => uploadPhotos(vehicle.id, items.map(p => p.file)),
    onSuccess: res => {
      setError(res.failed.length ? t(`Couldn't upload: ${res.failed.join(', ')}`, `Hochladen fehlgeschlagen: ${res.failed.join(', ')}`) : null)
      setPending([])
      refresh()
    },
  })

  const setCover = useMutation({
    mutationFn: (p: Photo) => api.put(`/vehicles/${vehicle.id}`, { ...vehicle, coverPhotoId: p.id }),
    onSuccess: refresh,
  })

  const del = useMutation({ mutationFn: (p: Photo) => api.del(`/photos/${p.id}`), onSuccess: refresh })

  if (isLoading) return <div className="center"><Spinner /></div>
  return (
    <>
      <PhotoGrid
        photos={photos}
        pending={pending}
        uploading={upload.isPending}
        coverId={vehicle.coverPhotoId}
        onAdd={items => { setPending(items); upload.mutate(items) }}
        onDelete={p => del.mutate(p)}
        onSetCover={p => setCover.mutate(p)}
      />
      {error && <p className="error">{error}</p>}
      <ErrorText error={upload.error ?? setCover.error ?? del.error} />
      {photos.length === 0 && !pending.length && <p className="faint" style={{ marginTop: 12 }}>{t('Photos attached to entries show up here too.', 'Fotos aus Einträgen erscheinen auch hier.')}</p>}
    </>
  )
}
