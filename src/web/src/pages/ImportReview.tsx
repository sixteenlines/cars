import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, ExternalLink, FileText, Plus, RotateCw, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, documentFileUrl, type BillDocument, type EntryKind, type EntryRef } from '../api'
import { buildGroups, buildImport, initialState, isExtra, lineTotal, type ReviewLine, type ReviewState, type SplitMode } from '../billImport'
import { EntryIcon } from '../entryIcons'
import { TagInput } from '../components/TagInput'
import { ConfirmButton, Empty, ErrorText, Field, Spinner, TopBar } from '../components/ui'
import { fmtDate, fmtMoney } from '../format'
import { keys, useVehicles } from '../hooks'
import { kindLabel, plural, t } from '../i18n'
import { inboxKey } from './ImportUpload'

const MODES: { key: SplitMode; label: () => string }[] = [
  { key: 'single', label: () => t('One entry', 'Ein Eintrag') },
  { key: 'category', label: () => t('By category', 'Nach Kategorie') },
  { key: 'item', label: () => t('Each item', 'Jede Position') },
]
const KIND_OPTIONS: EntryKind[] = ['Mod', 'Maintenance', 'Expense', 'Event']
const lineTypeLabel = (type: string): string | undefined => ({
  shipping: t('Shipping', 'Versand'), fee: t('Fee', 'Gebühr'), discount: t('Discount', 'Rabatt'), labour: t('Labour', 'Arbeitszeit'),
} as Record<string, string>)[type]

export default function ImportReview() {
  const docId = Number(useParams().docId)
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { data: vehicles = [] } = useVehicles()
  const { data: doc, isLoading } = useQuery({
    queryKey: ['documents', docId],
    queryFn: () => api.get<BillDocument>(`/documents/${docId}`),
    refetchInterval: q => (q.state.data?.status === 'Processing' ? 1500 : false),
  })
  const [state, setState] = useState<ReviewState | null>(null)
  const [manual, setManual] = useState(false)
  const [preview, setPreview] = useState(false)

  // Build the editable state once the extraction is there (or when the user chooses manual input).
  useEffect(() => {
    if (!doc || state) return
    if (doc.status === 'Ready' || (doc.status === 'Failed' && manual)) {
      const s = initialState(doc.extraction, doc.vehicleId ?? (vehicles.length === 1 ? vehicles[0].id : null))
      if (doc.status === 'Failed') s.lines = [blankLine()]
      setState(s)
    }
  }, [doc, state, manual, vehicles])

  const retry = useMutation({
    mutationFn: () => api.post(`/documents/${docId}/retry`),
    onSuccess: () => { setState(null); setManual(false); qc.invalidateQueries({ queryKey: ['documents', docId] }) },
  })
  const remove = useMutation({
    mutationFn: () => api.del(`/documents/${docId}`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: inboxKey }); navigate('/import', { replace: true }) },
  })
  const save = useMutation({
    mutationFn: () => api.post<EntryRef[]>(`/documents/${docId}/import`, { entries: buildImport(state!) }),
    onSuccess: () => {
      const vid = state!.vehicleId!
      qc.invalidateQueries({ queryKey: inboxKey })
      qc.invalidateQueries({ queryKey: ['documents', docId] })
      qc.invalidateQueries({ queryKey: keys.entries(vid) })
      qc.invalidateQueries({ queryKey: keys.vehicle(vid) })
      qc.invalidateQueries({ queryKey: keys.vehicles })
      qc.invalidateQueries({ queryKey: keys.tags })
      navigate(`/vehicles/${vid}`, { replace: true })
    },
  })

  if (isLoading) return <div className="center"><Spinner /></div>
  if (!doc) return <><TopBar back="/import" title={t('Bill', 'Rechnung')} /><Empty title={t("This bill doesn't exist (anymore).", 'Diese Rechnung gibt es nicht (mehr).')} /></>

  const top = <TopBar back="/import" title={doc.extraction?.vendor ?? doc.fileName} />
  const billCard = <BillCard doc={doc} preview={preview} onTogglePreview={() => setPreview(p => !p)} />

  if (doc.status === 'Processing') {
    return (<>{top}{billCard}
      <div className="card card-pad center" style={{ minHeight: 220, marginTop: 12 }}>
        <div><Spinner size={32} /><h2 style={{ marginTop: 12 }}>{t('Reading the bill…', 'Rechnung wird gelesen…')}</h2>
          <p className="muted">{t('Usually 10–40 seconds. You can leave — it waits for you under “Import bill”.', 'Meist 10–40 Sekunden. Du kannst die Seite verlassen – sie wartet unter „Rechnung importieren“ auf dich.')}</p></div>
      </div></>)
  }

  if (doc.status === 'Imported') {
    return (<>{top}{billCard}
      <div className="card card-pad" style={{ marginTop: 12 }}>
        <div className="row"><CheckCircle2 size={22} color="var(--ok)" /><h2>{t('Imported', 'Importiert am')} {doc.importedAt ? fmtDate(doc.importedAt) : ''}</h2></div>
        <div className="list" style={{ marginTop: 8 }}>
          {doc.entries.map(e => <Link key={e.id} to={`/entries/${e.id}`} className="list-item" style={{ minHeight: 48, padding: '8px 0' }}>{e.title}</Link>)}
        </div>
      </div>
      <div style={{ marginTop: 16 }}>
        <ConfirmButton className="btn btn-danger btn-ghost btn-block" onConfirm={() => remove.mutate()} disabled={remove.isPending}
          confirmLabel={<><Trash2 size={18} /> {t('Tap again — entries stay, only the bill file is removed', 'Nochmal tippen – Einträge bleiben, nur die Rechnungsdatei wird gelöscht')}</>}>
          <Trash2 size={18} /> {t('Delete bill', 'Rechnung löschen')}
        </ConfirmButton>
        <ErrorText error={remove.error} />
      </div></>)
  }

  if (doc.status === 'Failed' && !state) {
    return (<>{top}{billCard}
      <div className="card card-pad stack" style={{ marginTop: 12 }}>
        <div className="row"><AlertTriangle size={22} color="var(--danger)" /><h2>{t("Couldn't read this bill", 'Rechnung konnte nicht gelesen werden')}</h2></div>
        <p className="muted" style={{ margin: 0 }}>{doc.error}</p>
        <div className="row wrap">
          <button className="btn" onClick={() => retry.mutate()} disabled={retry.isPending}><RotateCw size={18} /> {t('Try again', 'Nochmal versuchen')}</button>
          <button className="btn btn-primary" onClick={() => setManual(true)}>{t('Fill in manually', 'Manuell ausfüllen')}</button>
        </div>
        <ErrorText error={retry.error} />
      </div></>)
  }

  if (!state) return <div className="center"><Spinner /></div>
  return (
    <>
      {top}
      {billCard}
      <Review doc={doc} state={state} setState={setState} vehicles={vehicles} />
      <ErrorText error={save.error ?? remove.error} />
      <div style={{ marginTop: 16 }}>
        <ConfirmButton className="btn btn-danger btn-ghost btn-block" onConfirm={() => remove.mutate()} disabled={remove.isPending}
          confirmLabel={<><Trash2 size={18} /> {t('Tap again to discard', 'Nochmal tippen zum Verwerfen')}</>}>
          <Trash2 size={18} /> {t('Discard bill', 'Rechnung verwerfen')}
        </ConfirmButton>
      </div>
      <ImportBar state={state} saving={save.isPending} onSave={() => save.mutate()} />
    </>
  )
}

const blankLine = (): ReviewLine => ({
  key: `m${Math.random().toString(36).slice(2)}`, include: true, name: '', shortName: '', brand: '', partNumber: '', quantity: '1',
  unitPrice: '', lineType: 'part', category: null, purpose: 'repair',
})

function BillCard({ doc, preview, onTogglePreview }: { doc: BillDocument; preview: boolean; onTogglePreview: () => void }) {
  const isPdf = doc.contentType === 'application/pdf'
  return (
    <div className="card" style={{ overflow: 'hidden' }}>
      <div className="list-item">
        <span className="entry-icon" style={{ '--hue': 30 } as React.CSSProperties}><FileText size={22} /></span>
        <div className="main">
          <div className="t">{doc.fileName}</div>
          <div className="s">{[doc.extraction?.invoice_number && `#${doc.extraction.invoice_number}`, doc.extraction?.invoice_date && fmtDate(doc.extraction.invoice_date), doc.extraction?.grand_total != null && fmtMoney(doc.extraction.grand_total)].filter(Boolean).join(' · ') || `${t('Uploaded', 'Hochgeladen am')} ${fmtDate(doc.createdAt)}`}</div>
        </div>
        <button className="btn" onClick={onTogglePreview}>{preview ? t('Hide', 'Ausblenden') : t('Show', 'Anzeigen')}</button>
        <a className="icon-btn" href={documentFileUrl(doc.id)} target="_blank" rel="noreferrer" aria-label={t('Open bill in new tab', 'Rechnung in neuem Tab öffnen')}><ExternalLink size={18} /></a>
      </div>
      {preview && (isPdf
        ? <iframe className="bill-preview" src={documentFileUrl(doc.id)} title={t('Bill', 'Rechnung')} />
        : <img className="bill-preview img" src={documentFileUrl(doc.id)} alt={t('Bill', 'Rechnung')} />)}
    </div>
  )
}

function Review({ doc, state, setState, vehicles }: {
  doc: BillDocument; state: ReviewState; setState: (s: ReviewState) => void; vehicles: { id: number; name: string }[]
}) {
  const set = (patch: Partial<ReviewState>) => setState({ ...state, ...patch })
  const setLine = (key: string, patch: Partial<ReviewLine>) =>
    set({ lines: state.lines.map(l => (l.key === key ? { ...l, ...patch } : l)) })

  const groups = useMemo(() => buildGroups(state), [state])
  const itemsTotal = state.lines.filter(l => l.include).reduce((a, l) => a + lineTotal(l), 0)
  const billTotal = doc.extraction?.grand_total ?? null
  const totalsMatch = billTotal == null || Math.abs(itemsTotal - billTotal) < 0.05
  const hasExtras = state.lines.some(l => l.include && isExtra(l)) && state.mode !== 'single'

  return (
    <div className="form" style={{ marginTop: 12 }}>
      {doc.alreadyUploaded && <div className="notice">{t('You uploaded this exact file before — showing the earlier upload, no new AI call.', 'Genau diese Datei hast du schon mal hochgeladen – hier ist der frühere Upload, ohne neuen KI-Aufruf.')}</div>}
      {doc.possibleDuplicates.length > 0 && (
        <div className="notice warn">
          <AlertTriangle size={18} /> {t('Same invoice number as', 'Gleiche Rechnungsnummer wie')}{' '}
          {doc.possibleDuplicates.map((d, i) => <span key={d.id}>{i > 0 && ', '}<Link to={`/import/${d.id}`}>{d.fileName}</Link>{d.importedAt ? t(' (imported)', ' (importiert)') : ''}</span>)}.
          {t('Maybe a duplicate?', 'Vielleicht doppelt?')}
        </div>
      )}
      {doc.warnings.map(w => <div key={w} className="notice warn"><AlertTriangle size={18} /> {w}</div>)}

      <div className="grid-2">
        <Field label={t('Car *', 'Auto *')}>
          <select value={state.vehicleId ?? ''} onChange={e => set({ vehicleId: e.target.value ? Number(e.target.value) : null })}>
            <option value="">{t('Choose…', 'Auswählen…')}</option>
            {vehicles.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </Field>
        <Field label={t('Date', 'Datum')}><input type="date" value={state.date} onChange={e => set({ date: e.target.value })} /></Field>
        <Field label={t('Shop', 'Werkstatt / Händler')}><input value={state.vendor} onChange={e => set({ vendor: e.target.value })} /></Field>
        <Field label={t('Odometer (km)', 'km-Stand')}><input inputMode="numeric" value={state.odometer} onChange={e => set({ odometer: e.target.value })} /></Field>
      </div>
      <div className="seg">
        <button type="button" className={state.status === 'Done' ? 'on' : ''} onClick={() => set({ status: 'Done' })}>{t('Done / installed', 'Erledigt / verbaut')}</button>
        <button type="button" className={state.status === 'Planned' ? 'on' : ''} onClick={() => set({ status: 'Planned' })}>{t('Not installed yet', 'Noch nicht verbaut')}</button>
      </div>

      <div className="stack" style={{ gap: 8 }}>
        <div className="row"><h2 className="grow">{t('Items on the bill', 'Positionen der Rechnung')}</h2><span className="faint">{t('untick to skip', 'abwählen zum Überspringen')}</span></div>
        <div className="card list">
          {state.lines.map(l => (
            <div key={l.key} className={`bill-line ${l.include ? '' : 'off'}`}>
              <div className="row">
                <input type="checkbox" className="cb" checked={l.include} aria-label={t('Include', 'Übernehmen')} onChange={e => setLine(l.key, { include: e.target.checked })} />
                <input className="input grow name" value={l.name} placeholder={t('Item', 'Position')} onChange={e => setLine(l.key, { name: e.target.value, shortName: '' })} />
              </div>
              <div className="bill-line-grid">
                <label><span>{t('Qty', 'Menge')}</span><input className="input" inputMode="decimal" value={l.quantity} onChange={e => setLine(l.key, { quantity: e.target.value })} /></label>
                <label><span>{t('Unit €', 'Stück €')}</span><input className="input" inputMode="decimal" value={l.unitPrice} onChange={e => setLine(l.key, { unitPrice: e.target.value })} /></label>
                <label><span>{t('Brand', 'Marke')}</span><input className="input" value={l.brand} onChange={e => setLine(l.key, { brand: e.target.value })} /></label>
                <label><span>{t('Part no.', 'Teilenr.')}</span><input className="input" value={l.partNumber} onChange={e => setLine(l.key, { partNumber: e.target.value })} /></label>
              </div>
              <div className="row wrap" style={{ gap: 4, paddingLeft: 34 }}>
                {lineTypeLabel(l.lineType) && <span className="tag">{lineTypeLabel(l.lineType)}</span>}
                {l.category && <span className="tag">#{l.category}</span>}
                <span className="spacer" />
                <strong className="amount">{fmtMoney(lineTotal(l))}</strong>
              </div>
            </div>
          ))}
        </div>
        <button type="button" className="btn" onClick={() => set({ lines: [...state.lines, blankLine()] })}><Plus size={18} /> {t('Add item', 'Position hinzufügen')}</button>
        <div className={`totals ${totalsMatch ? '' : 'error'}`}>
          <span>{t('Selected', 'Ausgewählt')} {fmtMoney(itemsTotal)}</span>
          {billTotal != null && <span>{totalsMatch ? '✓ ' : ''}{t('Bill', 'Rechnung')} {fmtMoney(billTotal)}</span>}
        </div>
      </div>

      <div className="stack" style={{ gap: 10 }}>
        <h2>{t('Create entries', 'Einträge anlegen')}</h2>
        <div className="seg">
          {MODES.map(m => <button type="button" key={m.key} className={state.mode === m.key ? 'on' : ''} onClick={() => set({ mode: m.key })}>{m.label()}</button>)}
        </div>
        {hasExtras && (
          <label className="check">
            <input type="checkbox" checked={state.spreadExtras} onChange={e => set({ spreadExtras: e.target.checked })} />
            {t('Spread shipping, fees & discounts over the entries', 'Versand, Gebühren & Rabatte auf die Einträge verteilen')}
          </label>
        )}
        {groups.map(g => (
          <div key={g.key} className="card card-pad stack" style={{ gap: 8 }}>
            <div className="row" style={{ gap: 10 }}>
              <EntryIcon entry={{ kind: g.kind, title: g.title, tags: g.tags }} />
              <input className="input grow" value={g.title} aria-label={t('Entry title', 'Titel des Eintrags')}
                onChange={e => set({ titles: { ...state.titles, [g.key]: e.target.value } })} />
            </div>
            <div className="row">
              <select className="input" style={{ width: 'auto', minHeight: 38, padding: '4px 10px' }} value={g.kind} aria-label={t('Kind', 'Art')}
                onChange={e => set({ kinds: { ...state.kinds, [g.key]: e.target.value as EntryKind } })}>
                {KIND_OPTIONS.map(k => <option key={k} value={k}>{kindLabel(k)}</option>)}
              </select>
              <span className="faint grow">{plural(g.lines.length, ['item', 'Position'], ['items', 'Positionen'])}{g.extraShare ? t(` · incl. ${fmtMoney(g.extraShare)} shipping/fees`, ` · inkl. ${fmtMoney(g.extraShare)} Versand/Gebühren`) : ''}</span>
              <strong>{fmtMoney(g.total)}</strong>
            </div>
            {g.tags.length > 0 && <div className="row wrap" style={{ gap: 4 }}>{g.tags.map(t => <span key={t} className="tag">#{t}</span>)}</div>}
          </div>
        ))}
      </div>

      <Field label={t('Extra tags for all entries', 'Zusätzliche Tags für alle Einträge')}><TagInput value={state.tags} onChange={tags => set({ tags })} /></Field>
      <Field label={t('Notes', 'Notizen')}><textarea value={state.notes} onChange={e => set({ notes: e.target.value })} /></Field>
    </div>
  )
}

function ImportBar({ state, saving, onSave }: { state: ReviewState; saving: boolean; onSave: () => void }) {
  const groups = buildGroups(state)
  const total = groups.reduce((a, g) => a + g.total, 0)
  const disabled = saving || !state.vehicleId || groups.length === 0
  const n = plural(groups.length, ['entry', 'Eintrag'], ['entries', 'Einträge'])
  return (
    <div className="form-actions">
      <button className="btn btn-primary" disabled={disabled} onClick={onSave}>
        {saving ? <Spinner size={18} /> : !state.vehicleId ? t('Choose a car', 'Auto wählen') : `${t(`Import ${n}`, `${n} importieren`)} · ${fmtMoney(total)}`}
      </button>
    </div>
  )
}
