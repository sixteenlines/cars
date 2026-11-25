import { useMutation, useQueryClient } from '@tanstack/react-query'
import { FileText, Link2, Pencil } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, documentFileUrl, uploadPhotos, type Photo } from '../api'
import { EntryIcon } from '../entryIcons'
import { PhotoGrid, type PendingPhoto } from '../components/PhotoGrid'
import { Empty, ErrorText, Spinner, TopBar } from '../components/ui'
import { fmtDate, fmtKm, fmtMoney } from '../format'
import { keys, useEntry, useVehicle } from '../hooks'
import { kindLabel, t } from '../i18n'

export default function EntryDetail() {
  const entryId = Number(useParams().entryId)
  const { data: e, isLoading } = useEntry(entryId)
  const { data: vehicle } = useVehicle(e?.vehicleId ?? 0)
  const qc = useQueryClient()
  const [pending, setPending] = useState<PendingPhoto[]>([])

  const refresh = () => {
    if (!e) return
    qc.invalidateQueries({ queryKey: keys.entry(e.id) })
    qc.invalidateQueries({ queryKey: keys.entries(e.vehicleId) })
    qc.invalidateQueries({ queryKey: keys.photos(e.vehicleId) })
    qc.invalidateQueries({ queryKey: keys.vehicle(e.vehicleId) })
    qc.invalidateQueries({ queryKey: keys.vehicles })
  }

  const upload = useMutation({
    mutationFn: (items: PendingPhoto[]) => uploadPhotos(e!.vehicleId, items.map(p => p.file), e!.id),
    onSuccess: () => { setPending([]); refresh() },
  })
  const del = useMutation({ mutationFn: (p: Photo) => api.del(`/photos/${p.id}`), onSuccess: refresh })

  if (isLoading) return <div className="center"><Spinner /></div>
  if (!e) return <><TopBar back="/" title={t('Not found', 'Nicht gefunden')} /><Empty title={t("This entry doesn't exist (anymore).", 'Diesen Eintrag gibt es nicht (mehr).')} /></>

  const hasParts = e.lineItems.length > 0

  return (
    <>
      <TopBar back={`/vehicles/${e.vehicleId}`} title={vehicle?.name}>
        <Link to={`/entries/${e.id}/edit`} className="icon-btn" aria-label={t('Edit entry', 'Eintrag bearbeiten')}><Pencil size={20} /></Link>
      </TopBar>

      <div className="row" style={{ gap: 14, alignItems: 'flex-start', margin: '8px 0 16px' }}>
        <EntryIcon entry={e} size={34} large />
        <div className="grow">
          <div className="faint">{kindLabel(e.kind)}{e.status === 'Planned' && <> · <span className="planned-badge">{t('Planned', 'Geplant')}</span></>}</div>
          <h1 style={{ overflowWrap: 'anywhere' }}>{e.title}</h1>
          <div style={{ fontSize: '1.4rem', fontWeight: 750, marginTop: 4 }}>{fmtMoney(e.total)}</div>
        </div>
      </div>

      <div className="card card-pad">
        <dl className="kv" style={{ margin: 0 }}>
          <dt>{t('Date', 'Datum')}</dt><dd>{fmtDate(e.date)}{e.endDate ? ` – ${fmtDate(e.endDate)}` : ''}</dd>
          {e.odometer != null && <><dt>{t('Odometer', 'km-Stand')}</dt><dd>{fmtKm(e.odometer)}</dd></>}
          {e.vendor && <><dt>{t('Shop', 'Werkstatt')}</dt><dd>{e.vendor}</dd></>}
          {e.tags.length > 0 && <><dt>Tags</dt><dd><span className="row wrap" style={{ justifyContent: 'flex-end', gap: 4 }}>{e.tags.map(t => <span key={t} className="tag">#{t}</span>)}</span></dd></>}
        </dl>
      </div>

      {(e.relatedEntry || e.linkedEntries.length > 0) && (
        <div className="section">
          <h2>{t('Linked', 'Verknüpft')}</h2>
          <div className="card list">
            {[...(e.relatedEntry ? [e.relatedEntry] : []), ...e.linkedEntries].map(r => (
              <Link key={r.id} to={`/entries/${r.id}`} className="list-item" style={{ minHeight: 52 }}>
                <Link2 size={18} className="muted" /><div className="main"><div className="t">{r.title}</div></div>
              </Link>
            ))}
          </div>
        </div>
      )}

      {hasParts && (
        <div className="section">
          <h2>{t('Parts', 'Teile')}</h2>
          <div className="card list">
            {e.lineItems.map(l => (
              <div key={l.id} className="list-item" style={{ minHeight: 56 }}>
                <div className="main">
                  <div className="t" style={{ whiteSpace: 'normal' }}>{l.name}</div>
                  <div className="s">{[l.brand, l.partNumber, l.quantity !== 1 ? `${l.quantity} × ${fmtMoney(l.unitPrice)}` : null].filter(Boolean).join(' · ')}</div>
                </div>
                <div className="amount">{fmtMoney(l.total)}</div>
              </div>
            ))}
          </div>
          {e.manualTotal != null && <p className="faint">{t(`Total set manually (${fmtMoney(e.manualTotal)}) instead of the parts sum.`, `Summe manuell gesetzt (${fmtMoney(e.manualTotal)}) statt der Summe der Teile.`)}</p>}
        </div>
      )}

      {e.documents.length > 0 && (
        <div className="section">
          <h2>{t('Bill', 'Rechnung')}</h2>
          <div className="card list">
            {e.documents.map(d => (
              <a key={d.id} href={documentFileUrl(d.id)} target="_blank" rel="noreferrer" className="list-item" style={{ minHeight: 52 }}>
                <FileText size={20} className="muted" />
                <div className="main"><div className="t">{d.vendor ?? d.fileName}</div>
                  <div className="s">{[d.fileName, d.invoiceDate ? fmtDate(d.invoiceDate) : null].filter(Boolean).join(' · ')}</div></div>
              </a>
            ))}
          </div>
        </div>
      )}

      {e.notes && (
        <div className="section">
          <h2>{t('Notes', 'Notizen')}</h2>
          <div className="card card-pad" style={{ whiteSpace: 'pre-wrap' }}>{e.notes}</div>
        </div>
      )}

      <div className="section">
        <h2>{t('Photos', 'Fotos')}</h2>
        <PhotoGrid photos={e.photos} pending={pending} uploading={upload.isPending}
          onAdd={items => { setPending(items); upload.mutate(items) }}
          onDelete={p => del.mutate(p)} />
        <ErrorText error={upload.error ?? del.error} />
      </div>
    </>
  )
}
