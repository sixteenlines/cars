import { Link } from 'react-router-dom'
import { photoUrl, type Entry } from '../api'
import { EntryIcon } from '../entryIcons'
import { fmtDate, fmtMoney } from '../format'
import { kindLabel, t } from '../i18n'

export function EntryRow({ entry }: { entry: Entry }) {
  const photo = entry.photos[0]
  const sub = [entry.date ? fmtDate(entry.date) : null, entry.vendor ?? entry.location, entry.tags.slice(0, 3).map(t => `#${t}`).join(' ')]
    .filter(Boolean).join(' · ')
  return (
    <Link to={`/entries/${entry.id}`} className="list-item">
      {photo ? <img className="thumb" src={photoUrl(photo.id)} alt="" loading="lazy" /> : <EntryIcon entry={entry} />}
      <div className="main">
        <div className="t">{entry.title}</div>
        <div className="s">{sub || kindLabel(entry.kind)}</div>
      </div>
      <div className="amount">
        {entry.total ? fmtMoney(entry.total) : <span className="faint">–</span>}
        {entry.status === 'Planned' && <div><span className="planned-badge">{t('Planned', 'Geplant')}</span></div>}
      </div>
    </Link>
  )
}
