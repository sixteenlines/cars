import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, uploadPhotos, type Entry, type EntryInput, type EntryKind, type EntryStatus, type Photo } from '../api'
import { EntryIcon } from '../entryIcons'
import { PhotoGrid, type PendingPhoto } from '../components/PhotoGrid'
import { TagInput } from '../components/TagInput'
import { ConfirmButton, ErrorText, Field, Spinner, TopBar } from '../components/ui'
import { fmtMoney, parseDecimal, parseIntOrNull, toInput, today } from '../format'
import { keys, useEntries, useEntry, useVehicle } from '../hooks'

interface PartForm { key: string; name: string; brand: string; partNumber: string; quantity: string; unitPrice: string }

const newPart = (): PartForm => ({ key: Math.random().toString(36).slice(2), name: '', brand: '', partNumber: '', quantity: '1', unitPrice: '' })

const KINDS: { key: EntryKind; label: string }[] = [
  { key: 'Mod', label: 'Mod' }, { key: 'Maintenance', label: 'Maintenance' }, { key: 'Expense', label: 'Expense' },
]

export default function EntryForm() {
  const params = useParams()
  const [search] = useSearchParams()
  const entryId = Number(params.entryId ?? 0)
  const isEdit = entryId > 0
  const { data: existing } = useEntry(entryId)
  const vehicleId = isEdit ? existing?.vehicleId ?? 0 : Number(params.id)
  const { data: vehicle } = useVehicle(vehicleId)
  const { data: siblings = [] } = useEntries(vehicleId)
  const navigate = useNavigate()
  const qc = useQueryClient()

  const initialKind = (search.get('kind') as EntryKind) || 'Mod'
  const [kind, setKind] = useState<EntryKind>(KINDS.some(k => k.key === initialKind) ? initialKind : 'Mod')
  const [status, setStatus] = useState<EntryStatus>('Done')
  const [title, setTitle] = useState('')
  const [date, setDate] = useState(today())
  const [endDate, setEndDate] = useState('')
  const [odometer, setOdometer] = useState('')
  const [vendor, setVendor] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [parts, setParts] = useState<PartForm[]>([])
  const [cost, setCost] = useState('')
  const [overrideTotal, setOverrideTotal] = useState(false)
  const [relatedId, setRelatedId] = useState('')
  const [notes, setNotes] = useState('')
  const [photos, setPhotos] = useState<Photo[]>([])
  const [pending, setPending] = useState<PendingPhoto[]>([])

  useEffect(() => {
    if (!existing) return
    setKind(existing.kind); setStatus(existing.status); setTitle(existing.title)
    setDate(existing.date ?? ''); setEndDate(existing.endDate ?? ''); setOdometer(existing.odometer?.toString() ?? '')
    setVendor(existing.vendor ?? ''); setTags(existing.tags); setNotes(existing.notes ?? '')
    setParts(existing.lineItems.map(l => ({
      key: String(l.id), name: l.name, brand: l.brand ?? '', partNumber: l.partNumber ?? '',
      quantity: toInput(l.quantity), unitPrice: toInput(l.unitPrice),
    })))
    setCost(toInput(existing.manualTotal))
    setOverrideTotal(existing.lineItems.length > 0 && existing.manualTotal != null)
    setRelatedId(existing.relatedEntry?.id.toString() ?? '')
    setPhotos(existing.photos)
  }, [existing])

  const partsSum = useMemo(() => parts.reduce((acc, p) => {
    const q = parseDecimal(p.quantity) ?? 1, u = parseDecimal(p.unitPrice) ?? 0
    return acc + (Number.isNaN(q) || Number.isNaN(u) ? 0 : Math.round(q * u * 100) / 100)
  }, 0), [parts])

  const updatePart = (key: string, patch: Partial<PartForm>) =>
    setParts(ps => ps.map(p => (p.key === key ? { ...p, ...patch } : p)))

  const save = useMutation({
    mutationFn: async () => {
      const manual = parts.length === 0 || overrideTotal ? parseDecimal(cost) : null
      if (Number.isNaN(manual)) throw new Error('Cost is not a valid number.')
      const lineItems = parts.filter(p => p.name.trim()).map(p => {
        const quantity = parseDecimal(p.quantity) ?? 1
        const unitPrice = parseDecimal(p.unitPrice) ?? 0
        if (Number.isNaN(quantity) || Number.isNaN(unitPrice)) throw new Error(`Check the numbers for “${p.name}”.`)
        return { name: p.name.trim(), brand: p.brand || null, partNumber: p.partNumber || null, quantity, unitPrice }
      })
      const body: EntryInput = {
        kind, status, title, date: date || null, endDate: endDate || null, odometer: parseIntOrNull(odometer),
        vendor: vendor || null, notes: notes || null, manualTotal: manual,
        relatedEntryId: relatedId ? Number(relatedId) : null, tags, lineItems,
      }
      const saved = isEdit
        ? await api.put<Entry>(`/entries/${entryId}`, body)
        : await api.post<Entry>(`/vehicles/${vehicleId}/entries`, body)
      if (pending.length) await uploadPhotos(vehicleId, pending.map(p => p.file), saved.id)
      return saved
    },
    onSuccess: saved => {
      qc.invalidateQueries({ queryKey: keys.entries(vehicleId) })
      qc.invalidateQueries({ queryKey: keys.entry(saved.id) })
      qc.invalidateQueries({ queryKey: keys.vehicle(vehicleId) })
      qc.invalidateQueries({ queryKey: keys.vehicles })
      qc.invalidateQueries({ queryKey: keys.photos(vehicleId) })
      qc.invalidateQueries({ queryKey: keys.tags })
      navigate(`/entries/${saved.id}`, { replace: true })
    },
  })

  const remove = useMutation({
    mutationFn: () => api.del(`/entries/${entryId}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.entries(vehicleId) })
      qc.invalidateQueries({ queryKey: keys.vehicle(vehicleId) })
      qc.invalidateQueries({ queryKey: keys.vehicles })
      navigate(`/vehicles/${vehicleId}`, { replace: true })
    },
  })

  const deletePhoto = useMutation({
    mutationFn: (p: Photo) => api.del(`/photos/${p.id}`),
    onSuccess: (_, p) => { setPhotos(ps => ps.filter(x => x.id !== p.id)); qc.invalidateQueries({ queryKey: keys.entry(entryId) }) },
  })

  if (isEdit && !existing) return <div className="center"><Spinner /></div>

  return (
    <>
      <TopBar back title={isEdit ? 'Edit entry' : `New entry · ${vehicle?.name ?? ''}`} />
      <form className="form" onSubmit={e => { e.preventDefault(); save.mutate() }}>
        <div className="seg" role="radiogroup" aria-label="Kind">
          {KINDS.map(k => (
            <button type="button" key={k.key} role="radio" aria-checked={kind === k.key} className={kind === k.key ? 'on' : ''}
              onClick={() => setKind(k.key)}>{k.label}</button>
          ))}
        </div>
        <div className="seg" role="radiogroup" aria-label="Status">
          <button type="button" role="radio" aria-checked={status === 'Done'} className={status === 'Done' ? 'on' : ''} onClick={() => setStatus('Done')}>Done</button>
          <button type="button" role="radio" aria-checked={status === 'Planned'} className={status === 'Planned' ? 'on' : ''} onClick={() => setStatus('Planned')}>Planned / wishlist</button>
        </div>

        <div className="row" style={{ alignItems: 'flex-end', gap: 12 }}>
          <EntryIcon entry={{ kind, title, tags }} />
          <div className="grow">
            <Field label="Title *">
              <input required maxLength={200} value={title} onChange={e => setTitle(e.target.value)}
                placeholder={kind === 'Mod' ? 'e.g. KW V3 coilovers' : kind === 'Maintenance' ? 'e.g. Oil change' : 'e.g. Insurance 2026'} />
            </Field>
          </div>
        </div>

        <div className="grid-2">
          <Field label={kind === 'Expense' ? 'Date / from' : 'Date'}>
            <input type="date" value={date} onChange={e => setDate(e.target.value)} />
          </Field>
          {kind === 'Expense'
            ? <Field label="Until (optional)"><input type="date" value={endDate} onChange={e => setEndDate(e.target.value)} /></Field>
            : <Field label="Odometer (km)"><input inputMode="numeric" value={odometer} onChange={e => setOdometer(e.target.value)} /></Field>}
          {kind === 'Expense' && <Field label="Odometer (km)"><input inputMode="numeric" value={odometer} onChange={e => setOdometer(e.target.value)} /></Field>}
          <Field label="Shop / vendor"><input value={vendor} onChange={e => setVendor(e.target.value)} /></Field>
          {(parts.length === 0 || overrideTotal) && (
            <Field label={parts.length ? 'Total (€)' : 'Cost (€)'}>
              <input inputMode="decimal" value={cost} onChange={e => setCost(e.target.value)} placeholder="0,00" />
            </Field>
          )}
        </div>

        <Field label="Tags"><TagInput value={tags} onChange={setTags} /></Field>

        <div className="stack" style={{ gap: 8 }}>
          <div className="row"><h2 className="grow">Parts</h2><span className="faint">optional</span></div>
          {parts.map((p, i) => (
            <div key={p.key} className="card part">
              <div className="part-head full">
                <strong className="grow faint">Part {i + 1}</strong>
                <button type="button" className="icon-btn" aria-label="Remove part" onClick={() => setParts(ps => ps.filter(x => x.key !== p.key))}><Trash2 size={18} /></button>
              </div>
              <label className="field full"><span>Name</span><input value={p.name} onChange={e => updatePart(p.key, { name: e.target.value })} /></label>
              <label className="field"><span>Brand</span><input value={p.brand} onChange={e => updatePart(p.key, { brand: e.target.value })} /></label>
              <label className="field"><span>Part no.</span><input value={p.partNumber} onChange={e => updatePart(p.key, { partNumber: e.target.value })} /></label>
              <label className="field"><span>Qty</span><input inputMode="decimal" value={p.quantity} onChange={e => updatePart(p.key, { quantity: e.target.value })} /></label>
              <label className="field"><span>Unit price (€)</span><input inputMode="decimal" value={p.unitPrice} onChange={e => updatePart(p.key, { unitPrice: e.target.value })} /></label>
            </div>
          ))}
          <button type="button" className="btn" onClick={() => setParts(ps => [...ps, newPart()])}><Plus size={18} /> Add part</button>
          {parts.length > 0 && (
            <>
              <div className="totals"><span>Parts total</span><span>{fmtMoney(partsSum)}</span></div>
              <label className="check">
                <input type="checkbox" checked={overrideTotal} onChange={e => setOverrideTotal(e.target.checked)} />
                Use a different total (shipping, discount…)
              </label>
            </>
          )}
        </div>

        <Field label="Belongs to" hint="E.g. link an installation to the mod it was for.">
          <select value={relatedId} onChange={e => setRelatedId(e.target.value)}>
            <option value="">—</option>
            {siblings.filter(s => s.id !== entryId).map(s => <option key={s.id} value={s.id}>{s.title}</option>)}
          </select>
        </Field>

        <Field label="Notes"><textarea value={notes} onChange={e => setNotes(e.target.value)} /></Field>

        <div className="stack" style={{ gap: 8 }}>
          <h2>Photos</h2>
          <PhotoGrid photos={photos} pending={pending}
            onAdd={items => setPending(ps => [...ps, ...items])}
            onRemovePending={key => setPending(ps => ps.filter(p => p.key !== key))}
            onDelete={isEdit ? p => deletePhoto.mutate(p) : undefined} />
        </div>

        <ErrorText error={save.error ?? remove.error} />
        {isEdit && (
          <ConfirmButton onConfirm={() => remove.mutate()} confirmLabel="Tap again to delete (photos stay in the gallery)">Delete entry</ConfirmButton>
        )}
        <div className="form-actions">
          <button type="button" className="btn" onClick={() => navigate(-1)}>Cancel</button>
          <button className="btn btn-primary" disabled={save.isPending}>
            {save.isPending ? <><Spinner size={18} /> {pending.length ? 'Uploading…' : 'Saving…'}</> : 'Save'}
          </button>
        </div>
      </form>
    </>
  )
}
