import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Camera, FileText, FileUp } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api, uploadDocument, type BillListItem } from '../api'
import { ErrorText, Spinner, TopBar } from '../components/ui'
import { fmtDate, fmtMoney } from '../format'
import { useVehicles } from '../hooks'
import { t } from '../i18n'

export const inboxKey = ['documents', 'open'] as const
export const useInbox = () =>
  useQuery({
    queryKey: inboxKey,
    queryFn: () => api.get<BillListItem[]>('/documents?open=true'),
    refetchInterval: q => (q.state.data?.some(d => d.status === 'Processing') ? 2000 : false),
  })

const statusLabel = (s: BillListItem['status']) => ({
  Processing: t('Reading…', 'Wird gelesen…'), Ready: t('Ready to review', 'Bereit zum Prüfen'),
  Failed: t('Needs manual input', 'Manuelle Eingabe nötig'), Imported: t('Imported', 'Importiert'),
})[s]

export default function ImportUpload() {
  const [params] = useSearchParams()
  const vehicleId = Number(params.get('vehicleId') ?? 0) || null
  const { data: vehicles = [] } = useVehicles()
  const { data: inbox = [] } = useInbox()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const fileInput = useRef<HTMLInputElement>(null)
  const cameraInput = useRef<HTMLInputElement>(null)
  const [over, setOver] = useState(false)
  const [busy, setBusy] = useState(0)
  const [error, setError] = useState<unknown>(null)

  const upload = async (files: FileList | File[] | null) => {
    const list = Array.from(files ?? [])
    if (!list.length) return
    setError(null)
    setBusy(list.length)
    let lastId: number | null = null
    for (const file of list) {
      try {
        const doc = await uploadDocument(file, vehicleId)
        lastId = doc.id
      } catch (e) {
        setError(e)
      }
      setBusy(n => n - 1)
    }
    qc.invalidateQueries({ queryKey: inboxKey })
    // One bill → straight to its review. Several → they wait in the list below.
    if (list.length === 1 && lastId) navigate(`/import/${lastId}`)
  }

  const vehicleName = (id: number | null) => vehicles.find(v => v.id === id)?.name

  return (
    <>
      <TopBar back={vehicleId ? `/vehicles/${vehicleId}` : '/'} title={t('Import bill', 'Rechnung importieren')} />

      <div
        className={`card dropzone ${over ? 'over' : ''}`}
        onDragOver={e => { e.preventDefault(); setOver(true) }}
        onDragLeave={() => setOver(false)}
        onDrop={e => { e.preventDefault(); setOver(false); upload(e.dataTransfer.files) }}
      >
        {busy > 0 ? <Spinner size={36} /> : <FileUp size={40} />}
        <h2>{busy > 0 ? `${t('Uploading', 'Lädt hoch')}${busy > 1 ? t(` (${busy} left)`, ` (noch ${busy})`) : ''}…` : t('Add a bill', 'Rechnung hinzufügen')}</h2>
        <p className="muted" style={{ margin: 0 }}>
          {t('PDF invoice or a photo of a receipt. Parts, prices and shop are filled in for you',
            'PDF-Rechnung oder Foto eines Belegs. Teile, Preise und Werkstatt werden automatisch ausgefüllt')}
          {vehicleId && vehicleName(vehicleId) ? <> {t('for', 'für')} <strong>{vehicleName(vehicleId)}</strong></> : null}.
        </p>
        <div className="row wrap" style={{ justifyContent: 'center', marginTop: 8 }}>
          <button className="btn btn-primary" disabled={busy > 0} onClick={() => fileInput.current?.click()}>
            <FileText size={18} /> {t('Choose file', 'Datei wählen')}
          </button>
          <button className="btn" disabled={busy > 0} onClick={() => cameraInput.current?.click()}>
            <Camera size={18} /> {t('Photo', 'Foto')}
          </button>
        </div>
        <p className="faint hide-touch" style={{ margin: 0 }}>{t('…or drop files here', '…oder Dateien hierher ziehen')}</p>
        <input ref={fileInput} type="file" hidden multiple accept="application/pdf,.pdf,image/*,.heic,.heif"
          onChange={e => { upload(e.target.files); e.target.value = '' }} />
        <input ref={cameraInput} type="file" hidden accept="image/*" capture="environment"
          onChange={e => { upload(e.target.files); e.target.value = '' }} />
      </div>
      <ErrorText error={error} />

      {inbox.length > 0 && (
        <div className="section">
          <h2>{t('Waiting for review', 'Warten auf Prüfung')}</h2>
          <div className="card list">
            {inbox.map(d => <InboxRow key={d.id} doc={d} vehicleName={vehicleName(d.vehicleId)} />)}
          </div>
        </div>
      )}
    </>
  )
}

export function InboxRow({ doc, vehicleName }: { doc: BillListItem; vehicleName?: string }) {
  return (
    <Link to={`/import/${doc.id}`} className="list-item">
      <span className="entry-icon" style={{ '--hue': 30 } as React.CSSProperties}>
        {doc.status === 'Processing' ? <Spinner size={20} /> : <FileText size={22} />}
      </span>
      <div className="main">
        <div className="t">{doc.vendor ?? doc.fileName}</div>
        <div className="s">{[statusLabel(doc.status), vehicleName, doc.invoiceDate ? fmtDate(doc.invoiceDate) : null].filter(Boolean).join(' · ')}</div>
      </div>
      {doc.total != null && <div className="amount">{fmtMoney(doc.total)}</div>}
    </Link>
  )
}
