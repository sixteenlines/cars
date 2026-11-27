import { CalendarClock, Camera, Flag, Fuel, KeyRound } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { photoUrl, type Entry, type Photo, type Vehicle } from '../api'
import { EntryIcon } from '../entryIcons'
import { fmtDate, fmtKm, fmtMoney, fmtMoneyShort, fmtNum } from '../format'
import { useEntries, useFuel, usePhotos } from '../hooks'
import { isDe, kindLabel, plural, t } from '../i18n'
import { buildTimeline, type TimelineFilter, type TimelineItem } from '../timeline'
import { Lightbox } from './PhotoGrid'
import { Empty, Spinner } from './ui'

const monthName = (month: number) =>
  new Intl.DateTimeFormat(isDe() ? 'de-DE' : 'en-GB', { month: 'long' }).format(new Date(2000, month - 1, 1))

/** "14.03." — the year is in the section header already. */
const dayMonth = (iso: string) => fmtDate(iso).slice(0, isDe() ? 6 : 5)

const FILTERS: { key: keyof TimelineFilter; label: () => string }[] = [
  { key: 'entries', label: () => t('Entries', 'Einträge') },
  { key: 'fuel', label: () => t('Fuel', 'Tanken') },
  { key: 'photos', label: () => t('Photos', 'Fotos') },
]

export function TimelineTab({ vehicle }: { vehicle: Vehicle }) {
  const entries = useEntries(vehicle.id)
  const fuel = useFuel(vehicle.id)
  const photos = usePhotos(vehicle.id)
  const [filter, setFilter] = useState<TimelineFilter>({ entries: true, fuel: true, photos: true })
  const [viewer, setViewer] = useState<{ photos: Photo[]; index: number } | null>(null)

  const timeline = useMemo(() => entries.data && fuel.data && photos.data
    ? buildTimeline(vehicle, entries.data, fuel.data.fuelUps, photos.data, filter) : null,
  [vehicle, entries.data, fuel.data, photos.data, filter])

  if (!timeline) return <div className="center"><Spinner /></div>
  const open = (list: Photo[], index: number) => setViewer({ photos: list, index })
  const empty = timeline.years.length === 0 && !timeline.undated.length && !timeline.undatedPlanned.length

  return (
    <>
      <div className="chips" style={{ marginBottom: 12 }}>
        {FILTERS.map(f => (
          <button key={f.key} className={`chip ${filter[f.key] ? 'active' : ''}`} aria-pressed={filter[f.key]}
            onClick={() => setFilter(x => ({ ...x, [f.key]: !x[f.key] }))}>{f.label()}</button>
        ))}
      </div>

      {empty ? (
        <div className="card"><Empty icon={<CalendarClock size={36} />} title={t('Nothing here yet', 'Noch nichts da')}>
          <p>{t('Entries, fill-ups and photos show up here by date.', 'Einträge, Tankfüllungen und Fotos erscheinen hier nach Datum.')}</p>
        </Empty></div>
      ) : (
        <div className="timeline">
          {timeline.undatedPlanned.length > 0 && (
            <section>
              <h2 className="tl-year"><span>{t('Planned', 'Geplant')}</span></h2>
              <ol className="tl-items">{timeline.undatedPlanned.map(e => <EntryItem key={e.id} entry={e} onPhoto={open} />)}</ol>
            </section>
          )}
          {timeline.years.map(y => (
            <section key={y.year}>
              <h2 className="tl-year"><span>{y.year}</span>{y.spent > 0 && <span className="faint">{fmtMoneyShort(y.spent)}</span>}</h2>
              {y.months.map(m => (
                <div key={m.key}>
                  <h3 className="tl-month">{monthName(m.month)}</h3>
                  <ol className="tl-items">{m.items.map(it => <Item key={itemKey(it)} item={it} vehicleId={vehicle.id} onPhoto={open} />)}</ol>
                </div>
              ))}
            </section>
          ))}
          {timeline.undated.length > 0 && (
            <section>
              <h2 className="tl-year"><span>{t('No date', 'Ohne Datum')}</span></h2>
              <ol className="tl-items">{timeline.undated.map(e => <EntryItem key={e.id} entry={e} onPhoto={open} />)}</ol>
            </section>
          )}
        </div>
      )}

      {viewer && (
        <Lightbox photos={viewer.photos} index={viewer.index} onIndex={i => setViewer({ ...viewer, index: i })} onClose={() => setViewer(null)} />
      )}
    </>
  )
}

const itemKey = (it: TimelineItem) =>
  it.type === 'entry' ? `e${it.entry.id}` : it.type === 'fuel' ? `f${it.date}` : it.type === 'photos' ? `p${it.date}` : `${it.type}${it.date}${'km' in it ? it.km : ''}`

type OnPhoto = (photos: Photo[], index: number) => void

function Item({ item, vehicleId, onPhoto }: { item: TimelineItem; vehicleId: number; onPhoto: OnPhoto }) {
  switch (item.type) {
    case 'entry': return <EntryItem entry={item.entry} onPhoto={onPhoto} />
    case 'fuel': return (
      <li className="tl-item">
        <span className="tl-dot entry-icon" style={{ '--hue': 140 } as React.CSSProperties}><Fuel size={20} /></span>
        <Link to={`/vehicles/${vehicleId}?tab=fuel`} className="tl-body">
          <div className="tl-row">
            <span className="t">{plural(item.fuelUps.length, ['fill-up', 'Tankfüllung'], ['fill-ups', 'Tankfüllungen'])}</span>
            <span className="amount">{fmtMoney(item.total)}</span>
          </div>
          <div className="s">{[
            item.fuelUps.length === 1 ? dayMonth(item.date) : null,
            item.liters > 0 ? `${fmtNum(item.liters, 1)} L` : null,
            item.avgConsumption != null ? `Ø ${fmtNum(item.avgConsumption, 1)} L/100 km` : null,
          ].filter(Boolean).join(' · ')}</div>
        </Link>
      </li>
    )
    case 'photos': return (
      <li className="tl-item">
        <span className="tl-dot entry-icon" style={{ '--hue': 200 } as React.CSSProperties}><Camera size={20} /></span>
        <div className="tl-body">
          <div className="tl-row"><span className="t">{plural(item.photos.length, ['photo', 'Foto'], ['photos', 'Fotos'])}</span><span className="faint">{dayMonth(item.date)}</span></div>
          <Thumbs photos={item.photos} onPhoto={onPhoto} />
        </div>
      </li>
    )
    case 'km': return (
      <li className="tl-item tl-milestone">
        <span className="tl-dot tl-dot-sm"><Flag size={14} /></span>
        <div className="tl-body"><span className="t">{t(`${fmtKm(item.km)} reached`, `${fmtKm(item.km)} erreicht`)}</span> <span className="faint">· {dayMonth(item.date)}</span></div>
      </li>
    )
    case 'purchase': return (
      <li className="tl-item tl-milestone">
        <span className="tl-dot entry-icon" style={{ '--hue': 30 } as React.CSSProperties}><KeyRound size={20} /></span>
        <div className="tl-body">
          <div className="tl-row"><span className="t">{t('Bought', 'Gekauft')}</span>{item.price != null && <span className="amount">{fmtMoney(item.price)}</span>}</div>
          <div className="s">{[fmtDate(item.date), item.odometer != null ? fmtKm(item.odometer) : null].filter(Boolean).join(' · ')}</div>
        </div>
      </li>
    )
  }
}

function EntryItem({ entry: e, onPhoto }: { entry: Entry; onPhoto: OnPhoto }) {
  const sub = [kindLabel(e.kind), e.date ? dayMonth(e.date) : null, e.odometer != null ? fmtKm(e.odometer) : null, e.vendor ?? e.location]
    .filter(Boolean).join(' · ')
  return (
    <li className={`tl-item ${e.status === 'Planned' ? 'tl-planned' : ''}`}>
      <span className="tl-dot"><EntryIcon entry={e} size={20} /></span>
      <div className="tl-body">
        <Link to={`/entries/${e.id}`} className="tl-link">
          <div className="tl-row">
            <span className="t">{e.title}</span>
            <span className="amount">{e.total ? fmtMoney(e.total) : ''}</span>
          </div>
          <div className="s">{sub}{e.status === 'Planned' && <> <span className="planned-badge">{t('Planned', 'Geplant')}</span></>}</div>
        </Link>
        {e.photos.length > 0 && <Thumbs photos={e.photos} onPhoto={onPhoto} />}
      </div>
    </li>
  )
}

function Thumbs({ photos, onPhoto }: { photos: Photo[]; onPhoto: OnPhoto }) {
  const shown = photos.slice(0, 6)
  return (
    <div className="tl-thumbs">
      {shown.map((p, i) => (
        <button key={p.id} type="button" onClick={() => onPhoto(photos, i)} aria-label={t('Open photo', 'Foto öffnen')}>
          <img src={photoUrl(p.id)} alt={p.caption ?? ''} loading="lazy" />
          {i === shown.length - 1 && photos.length > shown.length && <span className="more">+{photos.length - shown.length}</span>}
        </button>
      ))}
    </div>
  )
}

