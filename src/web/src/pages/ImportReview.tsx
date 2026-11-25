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
import { inboxKey } from './ImportUpload'

const MODES: { key: SplitMode; label: string }[] = [
  { key: 'single', label: 'One entry' }, { key: 'category', label: 'By category' }, { key: 'item', label: 'Each item' },
]
const KIND_OPTIONS: EntryKind[] = ['Mod', 'Maintenance', 'Expense']
const LINE_TYPE_LABEL: Record<string, string> = { shipping: 'Shipping', fee: 'Fee', discount: 'Discount', labour: 'Labour' }

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
  if (!doc) return <><TopBar back="/import" title="Bill" /><Empty title="This bill doesn't exist (anymore)." /></>

  const top = <TopBar back="/import" title={doc.extraction?.vendor ?? doc.fileName} />
  const billCard = <BillCard doc={doc} preview={preview} onTogglePreview={() => setPreview(p => !p)} />

  if (doc.status === 'Processing') {
    return (<>{top}{billCard}
      <div className="card card-pad center" style={{ minHeight: 220, marginTop: 12 }}>
        <div><Spinner size={32} /><h2 style={{ marginTop: 12 }}>Reading the bill…</h2>
          <p className="muted">Usually 10–40 seconds. You can leave — it waits for you under “Import bill”.</p></div>
      </div></>)
  }

  if (doc.status === 'Imported') {
    return (<>{top}{billCard}
      <div className="card card-pad" style={{ marginTop: 12 }}>
        <div className="row"><CheckCircle2 size={22} color="var(--ok)" /><h2>Imported {doc.importedAt ? fmtDate(doc.importedAt) : ''}</h2></div>
        <div className="list" style={{ marginTop: 8 }}>
          {doc.entries.map(e => <Link key={e.id} to={`/entries/${e.id}`} className="list-item" style={{ minHeight: 48, padding: '8px 0' }}>{e.title}</Link>)}
        </div>
      </div>
      <div style={{ marginTop: 16 }}>
        <ConfirmButton className="btn btn-danger btn-ghost btn-block" onConfirm={() => remove.mutate()} disabled={remove.isPending}
          confirmLabel={<><Trash2 size={18} /> Tap again — entries stay, only the bill file is removed</>}>
          <Trash2 size={18} /> Delete bill
        </ConfirmButton>
        <ErrorText error={remove.error} />
      </div></>)
  }

  if (doc.status === 'Failed' && !state) {
    return (<>{top}{billCard}
      <div className="card card-pad stack" style={{ marginTop: 12 }}>
        <div className="row"><AlertTriangle size={22} color="var(--danger)" /><h2>Couldn't read this bill</h2></div>
        <p className="muted" style={{ margin: 0 }}>{doc.error}</p>
        <div className="row wrap">
          <button className="btn" onClick={() => retry.mutate()} disabled={retry.isPending}><RotateCw size={18} /> Try again</button>
          <button className="btn btn-primary" onClick={() => setManual(true)}>Fill in manually</button>
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
          confirmLabel={<><Trash2 size={18} /> Tap again to discard</>}>
          <Trash2 size={18} /> Discard bill
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
          <div className="s">{[doc.extraction?.invoice_number && `#${doc.extraction.invoice_number}`, doc.extraction?.invoice_date && fmtDate(doc.extraction.invoice_date), doc.extraction?.grand_total != null && fmtMoney(doc.extraction.grand_total)].filter(Boolean).join(' · ') || 'Uploaded ' + fmtDate(doc.createdAt)}</div>
        </div>
        <button className="btn" onClick={onTogglePreview}>{preview ? 'Hide' : 'Show'}</button>
        <a className="icon-btn" href={documentFileUrl(doc.id)} target="_blank" rel="noreferrer" aria-label="Open bill in new tab"><ExternalLink size={18} /></a>
      </div>
      {preview && (isPdf
        ? <iframe className="bill-preview" src={documentFileUrl(doc.id)} title="Bill" />
        : <img className="bill-preview img" src={documentFileUrl(doc.id)} alt="Bill" />)}
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
      {doc.alreadyUploaded && <div className="notice">You uploaded this exact file before — showing the earlier upload, no new AI call.</div>}
      {doc.possibleDuplicates.length > 0 && (
        <div className="notice warn">
          <AlertTriangle size={18} /> Same invoice number as{' '}
          {doc.possibleDuplicates.map((d, i) => <span key={d.id}>{i > 0 && ', '}<Link to={`/import/${d.id}`}>{d.fileName}</Link>{d.importedAt ? ' (imported)' : ''}</span>)}.
          Maybe a duplicate?
        </div>
      )}
      {doc.warnings.map(w => <div key={w} className="notice warn"><AlertTriangle size={18} /> {w}</div>)}

      <div className="grid-2">
        <Field label="Car *">
          <select value={state.vehicleId ?? ''} onChange={e => set({ vehicleId: e.target.value ? Number(e.target.value) : null })}>
            <option value="">Choose…</option>
            {vehicles.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </Field>
        <Field label="Date"><input type="date" value={state.date} onChange={e => set({ date: e.target.value })} /></Field>
        <Field label="Shop"><input value={state.vendor} onChange={e => set({ vendor: e.target.value })} /></Field>
        <Field label="Odometer (km)"><input inputMode="numeric" value={state.odometer} onChange={e => set({ odometer: e.target.value })} /></Field>
      </div>
      <div className="seg">
        <button type="button" className={state.status === 'Done' ? 'on' : ''} onClick={() => set({ status: 'Done' })}>Done / installed</button>
        <button type="button" className={state.status === 'Planned' ? 'on' : ''} onClick={() => set({ status: 'Planned' })}>Not installed yet</button>
      </div>

      <div className="stack" style={{ gap: 8 }}>
        <div className="row"><h2 className="grow">Items on the bill</h2><span className="faint">untick to skip</span></div>
        <div className="card list">
          {state.lines.map(l => (
            <div key={l.key} className={`bill-line ${l.include ? '' : 'off'}`}>
              <div className="row">
                <input type="checkbox" className="cb" checked={l.include} aria-label="Include" onChange={e => setLine(l.key, { include: e.target.checked })} />
                <input className="input grow name" value={l.name} placeholder="Item" onChange={e => setLine(l.key, { name: e.target.value, shortName: '' })} />
              </div>
              <div className="bill-line-grid">
                <label><span>Qty</span><input className="input" inputMode="decimal" value={l.quantity} onChange={e => setLine(l.key, { quantity: e.target.value })} /></label>
                <label><span>Unit €</span><input className="input" inputMode="decimal" value={l.unitPrice} onChange={e => setLine(l.key, { unitPrice: e.target.value })} /></label>
                <label><span>Brand</span><input className="input" value={l.brand} onChange={e => setLine(l.key, { brand: e.target.value })} /></label>
                <label><span>Part no.</span><input className="input" value={l.partNumber} onChange={e => setLine(l.key, { partNumber: e.target.value })} /></label>
              </div>
              <div className="row wrap" style={{ gap: 4, paddingLeft: 34 }}>
                {LINE_TYPE_LABEL[l.lineType] && <span className="tag">{LINE_TYPE_LABEL[l.lineType]}</span>}
                {l.category && <span className="tag">#{l.category}</span>}
                <span className="spacer" />
                <strong className="amount">{fmtMoney(lineTotal(l))}</strong>
              </div>
            </div>
          ))}
        </div>
        <button type="button" className="btn" onClick={() => set({ lines: [...state.lines, blankLine()] })}><Plus size={18} /> Add item</button>
        <div className={`totals ${totalsMatch ? '' : 'error'}`}>
          <span>Selected {fmtMoney(itemsTotal)}</span>
          {billTotal != null && <span>{totalsMatch ? '✓ ' : ''}Bill {fmtMoney(billTotal)}</span>}
        </div>
      </div>

      <div className="stack" style={{ gap: 10 }}>
        <h2>Create entries</h2>
        <div className="seg">
          {MODES.map(m => <button type="button" key={m.key} className={state.mode === m.key ? 'on' : ''} onClick={() => set({ mode: m.key })}>{m.label}</button>)}
        </div>
        {hasExtras && (
          <label className="check">
            <input type="checkbox" checked={state.spreadExtras} onChange={e => set({ spreadExtras: e.target.checked })} />
            Spread shipping, fees & discounts over the entries
          </label>
        )}
        {groups.map(g => (
          <div key={g.key} className="card card-pad stack" style={{ gap: 8 }}>
            <div className="row" style={{ gap: 10 }}>
              <EntryIcon entry={{ kind: g.kind, title: g.title, tags: g.tags }} />
              <input className="input grow" value={g.title} aria-label="Entry title"
                onChange={e => set({ titles: { ...state.titles, [g.key]: e.target.value } })} />
            </div>
            <div className="row">
              <select className="input" style={{ width: 'auto', minHeight: 38, padding: '4px 10px' }} value={g.kind} aria-label="Kind"
                onChange={e => set({ kinds: { ...state.kinds, [g.key]: e.target.value as EntryKind } })}>
                {KIND_OPTIONS.map(k => <option key={k} value={k}>{k}</option>)}
              </select>
              <span className="faint grow">{g.lines.length} item{g.lines.length === 1 ? '' : 's'}{g.extraShare ? ` · incl. ${fmtMoney(g.extraShare)} shipping/fees` : ''}</span>
              <strong>{fmtMoney(g.total)}</strong>
            </div>
            {g.tags.length > 0 && <div className="row wrap" style={{ gap: 4 }}>{g.tags.map(t => <span key={t} className="tag">#{t}</span>)}</div>}
          </div>
        ))}
      </div>

      <Field label="Extra tags for all entries"><TagInput value={state.tags} onChange={tags => set({ tags })} /></Field>
      <Field label="Notes"><textarea value={state.notes} onChange={e => set({ notes: e.target.value })} /></Field>
    </div>
  )
}

function ImportBar({ state, saving, onSave }: { state: ReviewState; saving: boolean; onSave: () => void }) {
  const groups = buildGroups(state)
  const total = groups.reduce((a, g) => a + g.total, 0)
  const disabled = saving || !state.vehicleId || groups.length === 0
  return (
    <div className="form-actions">
      <button className="btn btn-primary" disabled={disabled} onClick={onSave}>
        {saving ? <Spinner size={18} /> : !state.vehicleId ? 'Choose a car' : `Import ${groups.length} entr${groups.length === 1 ? 'y' : 'ies'} · ${fmtMoney(total)}`}
      </button>
    </div>
  )
}
