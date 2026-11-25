import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type Vehicle, type VehicleInput } from '../api'
import { ConfirmButton, ErrorText, Field, Spinner, TopBar } from '../components/ui'
import { parseDecimal, parseIntOrNull, toInput } from '../format'
import { keys, useVehicle } from '../hooks'
import { t } from '../i18n'

type Form = Record<'name' | 'make' | 'model' | 'year' | 'badge' | 'colorExterior' | 'colorInterior' | 'plate' | 'vin'
  | 'purchasePrice' | 'purchaseDate' | 'purchaseOdometer' | 'currentOdometer' | 'notes', string> & { includePurchaseInTotals: boolean }

const empty: Form = {
  name: '', make: '', model: '', year: '', badge: '', colorExterior: '', colorInterior: '', plate: '', vin: '',
  purchasePrice: '', purchaseDate: '', purchaseOdometer: '', currentOdometer: '', notes: '', includePurchaseInTotals: false,
}

const fromVehicle = (v: Vehicle): Form => ({
  name: v.name, make: v.make ?? '', model: v.model ?? '', year: v.year?.toString() ?? '', badge: v.badge ?? '',
  colorExterior: v.colorExterior ?? '', colorInterior: v.colorInterior ?? '', plate: v.plate ?? '', vin: v.vin ?? '',
  purchasePrice: toInput(v.purchasePrice), purchaseDate: v.purchaseDate ?? '',
  purchaseOdometer: v.purchaseOdometer?.toString() ?? '', currentOdometer: v.currentOdometer?.toString() ?? '',
  notes: v.notes ?? '', includePurchaseInTotals: v.includePurchaseInTotals,
})

export default function VehicleForm() {
  const { id } = useParams()
  const vehicleId = Number(id ?? 0)
  const isEdit = vehicleId > 0
  const { data: vehicle } = useVehicle(vehicleId)
  const [f, setF] = useState<Form>(empty)
  const navigate = useNavigate()
  const qc = useQueryClient()

  useEffect(() => { if (vehicle) setF(fromVehicle(vehicle)) }, [vehicle])

  const set = (k: keyof Form) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setF(prev => ({ ...prev, [k]: e.target.type === 'checkbox' ? (e.target as HTMLInputElement).checked : e.target.value }))

  const save = useMutation({
    mutationFn: () => {
      const price = parseDecimal(f.purchasePrice)
      if (Number.isNaN(price)) throw new Error(t('Purchase price is not a number.', 'Der Kaufpreis ist keine Zahl.'))
      const body: VehicleInput = {
        name: f.name, make: f.make || null, model: f.model || null, year: parseIntOrNull(f.year), badge: f.badge || null,
        colorExterior: f.colorExterior || null, colorInterior: f.colorInterior || null, plate: f.plate || null, vin: f.vin || null,
        purchasePrice: price, purchaseDate: f.purchaseDate || null, purchaseOdometer: parseIntOrNull(f.purchaseOdometer),
        currentOdometer: parseIntOrNull(f.currentOdometer), includePurchaseInTotals: f.includePurchaseInTotals,
        notes: f.notes || null, coverPhotoId: vehicle?.coverPhotoId ?? null,
      }
      return isEdit ? api.put<Vehicle>(`/vehicles/${vehicleId}`, body) : api.post<Vehicle>('/vehicles', body)
    },
    onSuccess: v => {
      qc.invalidateQueries({ queryKey: keys.vehicles })
      navigate(`/vehicles/${v.id}`, { replace: true })
    },
  })

  const remove = useMutation({
    mutationFn: () => api.del(`/vehicles/${vehicleId}`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: keys.vehicles }); navigate('/', { replace: true }) },
  })

  if (isEdit && !vehicle) return <div className="center"><Spinner /></div>

  return (
    <>
      <TopBar back title={isEdit ? t(`Edit ${vehicle?.name}`, `${vehicle?.name} bearbeiten`) : t('New car', 'Neues Auto')} />
      <form className="form" onSubmit={e => { e.preventDefault(); save.mutate() }}>
        <Field label="Name *" hint={t('What you call it, e.g. “M2” or “Winter E46”.', 'Wie du es nennst, z. B. „M2“ oder „Winter-E46“.')}>
          <input required maxLength={100} value={f.name} onChange={set('name')} autoFocus={!isEdit} />
        </Field>
        <div className="grid-2">
          <Field label={t('Make', 'Marke')}><input value={f.make} onChange={set('make')} placeholder="BMW" /></Field>
          <Field label={t('Model', 'Modell')}><input value={f.model} onChange={set('model')} placeholder="M2" /></Field>
          <Field label={t('Year', 'Baujahr')}><input inputMode="numeric" value={f.year} onChange={set('year')} /></Field>
          <Field label={t('Variant / badge', 'Variante / Typ')}><input value={f.badge} onChange={set('badge')} placeholder="730d" /></Field>
          <Field label={t('Colour (outside)', 'Farbe (außen)')}><input value={f.colorExterior} onChange={set('colorExterior')} /></Field>
          <Field label={t('Colour (inside)', 'Farbe (innen)')}><input value={f.colorInterior} onChange={set('colorInterior')} /></Field>
          <Field label={t('Plate', 'Kennzeichen')}><input value={f.plate} onChange={set('plate')} autoCapitalize="characters" /></Field>
          <Field label={t('Current odometer (km)', 'Aktueller km-Stand')}><input inputMode="numeric" value={f.currentOdometer} onChange={set('currentOdometer')} /></Field>
        </div>
        <Field label={t('VIN', 'FIN')}><input value={f.vin} onChange={set('vin')} maxLength={17} autoCapitalize="characters" /></Field>

        <h2 style={{ marginTop: 8 }}>{t('Purchase', 'Kauf')}</h2>
        <div className="grid-2">
          <Field label={t('Price (€)', 'Preis (€)')}><input inputMode="decimal" value={f.purchasePrice} onChange={set('purchasePrice')} /></Field>
          <Field label={t('Date', 'Datum')}><input type="date" value={f.purchaseDate} onChange={set('purchaseDate')} /></Field>
          <Field label={t('Odometer (km)', 'km-Stand')}><input inputMode="numeric" value={f.purchaseOdometer} onChange={set('purchaseOdometer')} /></Field>
        </div>
        <label className="check">
          <input type="checkbox" checked={f.includePurchaseInTotals} onChange={set('includePurchaseInTotals')} />
          {t('Count purchase price in “total spent”', 'Kaufpreis in „Gesamtkosten“ einrechnen')}
        </label>
        <Field label={t('Notes', 'Notizen')}><textarea value={f.notes} onChange={set('notes')} /></Field>

        <ErrorText error={save.error ?? remove.error} />
        {isEdit && (
          <ConfirmButton onConfirm={() => remove.mutate()} confirmLabel={t(`Tap again: delete ${vehicle?.name} with all entries & photos`, `Nochmal tippen: ${vehicle?.name} mit allen Einträgen & Fotos löschen`)}>{t('Delete car', 'Auto löschen')}</ConfirmButton>
        )}
        <div className="form-actions">
          <button type="button" className="btn" onClick={() => navigate(-1)}>{t('Cancel', 'Abbrechen')}</button>
          <button className="btn btn-primary" disabled={save.isPending}>{save.isPending ? <Spinner size={18} /> : t('Save', 'Speichern')}</button>
        </div>
      </form>
    </>
  )
}
