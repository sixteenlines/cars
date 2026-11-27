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
import { kindLabel, t } from '../i18n'

interface PartForm { key: string; name: string; brand: string; partNumber: string; quantity: string; unitPrice: string }

const newPart = (): PartForm => ({ key: Math.random().toString(36).slice(2), name: '', brand: '', partNumber: '', quantity: '1', unitPrice: '' })

const KINDS: { key: EntryKind }[] = [{ key: 'Mod' }, { key: 'Maintenance' }, { key: 'Expense' }, { key: 'Event' }]

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
  const [location, setLocation] = useState('')
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
    setVendor(existing.vendor ?? ''); setLocation(existing.location ?? ''); setTags(existing.tags); setNotes(existing.notes ?? '')
    setParts(existing.lineItems.map(l => ({
      key: String(l.id), name: l.name, brand: l.brand ?? '', partNumber: l.partNumber ?? '',
      quantity: toInput(l.quantity), unitPrice: toInput(l.unitPrice, 2),
    })))
    setCost(toInput(existing.manualTotal, 2))
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
      if (Number.isNaN(manual)) throw new Error(t('Cost is not a valid number.', 'Die Kosten sind keine gültige Zahl.'))
      const lineItems = parts.filter(p => p.name.trim()).map(p => {
        const quantity = parseDecimal(p.quantity) ?? 1
        const unitPrice = parseDecimal(p.unitPrice) ?? 0
        if (Number.isNaN(quantity) || Number.isNaN(unitPrice)) throw new Error(t(`Check the numbers for “${p.name}”.`, `Prüf die Zahlen bei „${p.name}“.`))
        return { name: p.name.trim(), brand: p.brand || null, partNumber: p.partNumber || null, quantity, unitPrice }
      })
      const body: EntryInput = {
        kind, status, title, date: date || null, endDate: endDate || null, odometer: parseIntOrNull(odometer),
        vendor: vendor || null, location: location || null, notes: notes || null, manualTotal: manual,
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
  /** Expenses and events can span a period (insurance year, a multi-day trip). */
  const spans = kind === 'Expense' || kind === 'Event'

  return (
    <>
      <TopBar back title={isEdit ? t('Edit entry', 'Eintrag bearbeiten') : `${t('New entry', 'Neuer Eintrag')} · ${vehicle?.name ?? ''}`} />
      <form className="form" onSubmit={e => { e.preventDefault(); save.mutate() }}>
        <div className="seg" role="radiogroup" aria-label={t('Kind', 'Art')}>
          {KINDS.map(k => (
            <button type="button" key={k.key} role="radio" aria-checked={kind === k.key} className={kind === k.key ? 'on' : ''}
              onClick={() => setKind(k.key)}>{kindLabel(k.key)}</button>
          ))}
        </div>
        <div className="seg" role="radiogroup" aria-label="Status">
          <button type="button" role="radio" aria-checked={status === 'Done'} className={status === 'Done' ? 'on' : ''} onClick={() => setStatus('Done')}>{t('Done', 'Erledigt')}</button>
          <button type="button" role="radio" aria-checked={status === 'Planned'} className={status === 'Planned' ? 'on' : ''} onClick={() => setStatus('Planned')}>{t('Planned / wishlist', 'Geplant / Wunschliste')}</button>
        </div>

        <div className="row" style={{ alignItems: 'flex-end', gap: 12 }}>
          <EntryIcon entry={{ kind, title, tags }} />
          <div className="grow">
            <Field label={t('Title *', 'Titel *')}>
              <input required maxLength={200} value={title} onChange={e => setTitle(e.target.value)}
                placeholder={kind === 'Mod' ? t('e.g. KW V3 coilovers', 'z. B. KW V3 Gewindefahrwerk') : kind === 'Maintenance' ? t('e.g. Oil change', 'z. B. Ölwechsel') : kind === 'Event' ? t('e.g. Track day Nürburgring', 'z. B. Trackday Nürburgring') : t('e.g. Insurance 2026', 'z. B. Versicherung 2026')} />
            </Field>
          </div>
        </div>

        <div className="grid-2">
          <Field label={spans ? t('Date / from', 'Datum / ab') : t('Date', 'Datum')}>
            <input type="date" value={date} onChange={e => setDate(e.target.value)} />
          </Field>
          {spans
            ? <Field label={t('Until (optional)', 'Bis (optional)')}><input type="date" value={endDate} onChange={e => setEndDate(e.target.value)} /></Field>
            : <Field label={t('Odometer (km)', 'km-Stand')}><input inputMode="numeric" value={odometer} onChange={e => setOdometer(e.target.value)} /></Field>}
          {spans && <Field label={t('Odometer (km)', 'km-Stand')}><input inputMode="numeric" value={odometer} onChange={e => setOdometer(e.target.value)} /></Field>}
          {kind === 'Event'
            ? <Field label={t('Location', 'Ort')}><input value={location} onChange={e => setLocation(e.target.value)} placeholder={t('e.g. Nürburgring', 'z. B. Nürburgring')} /></Field>
            : <Field label={t('Shop / vendor', 'Werkstatt / Händler')}><input value={vendor} onChange={e => setVendor(e.target.value)} /></Field>}
          {(parts.length === 0 || overrideTotal) && (
            <Field label={parts.length ? t('Total (€)', 'Summe (€)') : t('Cost (€)', 'Kosten (€)')}>
              <input inputMode="decimal" value={cost} onChange={e => setCost(e.target.value)} placeholder={t('0.00', '0,00')} />
            </Field>
          )}
        </div>

        <Field label="Tags"><TagInput value={tags} onChange={setTags} /></Field>

        <div className="stack" style={{ gap: 8 }}>
          <div className="row"><h2 className="grow">{t('Parts', 'Teile')}</h2><span className="faint">optional</span></div>
          {parts.map((p, i) => (
            <div key={p.key} className="card part">
              <div className="part-head full">
                <strong className="grow faint">{t('Part', 'Teil')} {i + 1}</strong>
                <button type="button" className="icon-btn" aria-label={t('Remove part', 'Teil entfernen')} onClick={() => setParts(ps => ps.filter(x => x.key !== p.key))}><Trash2 size={18} /></button>
              </div>
              <label className="field full"><span>{t('Name', 'Bezeichnung')}</span><input value={p.name} onChange={e => updatePart(p.key, { name: e.target.value })} /></label>
              <label className="field"><span>{t('Brand', 'Marke')}</span><input value={p.brand} onChange={e => updatePart(p.key, { brand: e.target.value })} /></label>
              <label className="field"><span>{t('Part no.', 'Teilenr.')}</span><input value={p.partNumber} onChange={e => updatePart(p.key, { partNumber: e.target.value })} /></label>
              <label className="field"><span>{t('Qty', 'Menge')}</span><input inputMode="decimal" value={p.quantity} onChange={e => updatePart(p.key, { quantity: e.target.value })} /></label>
              <label className="field"><span>{t('Unit price (€)', 'Stückpreis (€)')}</span><input inputMode="decimal" value={p.unitPrice} onChange={e => updatePart(p.key, { unitPrice: e.target.value })} /></label>
            </div>
          ))}
          <button type="button" className="btn" onClick={() => setParts(ps => [...ps, newPart()])}><Plus size={18} /> {t('Add part', 'Teil hinzufügen')}</button>
          {parts.length > 0 && (
            <>
              <div className="totals"><span>{t('Parts total', 'Summe Teile')}</span><span>{fmtMoney(partsSum)}</span></div>
              <label className="check">
                <input type="checkbox" checked={overrideTotal} onChange={e => setOverrideTotal(e.target.checked)} />
                {t('Use a different total (shipping, discount…)', 'Andere Summe verwenden (Versand, Rabatt…)')}
              </label>
            </>
          )}
        </div>

        <Field label={t('Belongs to', 'Gehört zu')} hint={t('E.g. link an installation to the mod it was for.', 'Z. B. den Einbau mit der passenden Mod verknüpfen.')}>
          <select value={relatedId} onChange={e => setRelatedId(e.target.value)}>
            <option value="">—</option>
            {siblings.filter(s => s.id !== entryId).map(s => <option key={s.id} value={s.id}>{s.title}</option>)}
          </select>
        </Field>

        <Field label={t('Notes', 'Notizen')}><textarea value={notes} onChange={e => setNotes(e.target.value)} /></Field>

        <div className="stack" style={{ gap: 8 }}>
          <h2>{t('Photos', 'Fotos')}</h2>
          <PhotoGrid photos={photos} pending={pending}
            onAdd={items => setPending(ps => [...ps, ...items])}
            onRemovePending={key => setPending(ps => ps.filter(p => p.key !== key))}
            onDelete={isEdit ? p => deletePhoto.mutate(p) : undefined} />
        </div>

        <ErrorText error={save.error ?? remove.error} />
        {isEdit && (
          <ConfirmButton onConfirm={() => remove.mutate()} confirmLabel={t('Tap again to delete (photos stay in the gallery)', 'Nochmal tippen zum Löschen (Fotos bleiben in der Galerie)')}>{t('Delete entry', 'Eintrag löschen')}</ConfirmButton>
        )}
        <div className="form-actions">
          <button type="button" className="btn" onClick={() => navigate(-1)}>{t('Cancel', 'Abbrechen')}</button>
          <button className="btn btn-primary" disabled={save.isPending}>
            {save.isPending ? <><Spinner size={18} /> {pending.length ? t('Uploading…', 'Lädt hoch…') : t('Saving…', 'Speichert…')}</> : t('Save', 'Speichern')}
          </button>
        </div>
      </form>
    </>
  )
}
