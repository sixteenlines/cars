import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Camera, Images, TriangleAlert } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, FUEL_GRADES, scanFuel, type FuelGrade, type FuelScan, type FuelUp, type FuelUpInput } from '../api'
import { ConfirmButton, ErrorText, Field, Spinner, TopBar } from '../components/ui'
import { fmtKm, fmtNum, parseDecimal, parseIntOrNull, toInput, today } from '../format'
import { keys, useFuel, useFuelUp, useVehicle } from '../hooks'
import { t } from '../i18n'

type Num = 'total' | 'ppl' | 'liters'
const ALL: Num[] = ['total', 'ppl', 'liters']
const DECIMALS: Record<Num, number> = { total: 2, ppl: 3, liters: 2 }

const show = (n: number | null | undefined, field: Num) => toInput(n, DECIMALS[field])
const pos = (s: string) => {
  const n = parseDecimal(s)
  return n != null && !Number.isNaN(n) && n > 0 ? n : null
}

/** The field that isn't among the two most recently typed ones gets calculated. */
const computedOf = (order: Num[]) => ALL.find(f => !order.slice(0, 2).includes(f))!

function recompute(v: Record<Num, string>, computed: Num): Record<Num, string> {
  const t = pos(v.total), p = pos(v.ppl), l = pos(v.liters)
  let value: number | null = null
  if (computed === 'total' && l && p) value = l * p
  if (computed === 'liters' && t && p) value = t / p
  if (computed === 'ppl' && t && l) value = t / l
  return { ...v, [computed]: value == null ? '' : show(value, computed) }
}

export default function FuelForm() {
  const params = useParams()
  const fuelId = Number(params.fuelId ?? 0)
  const isEdit = fuelId > 0
  const { data: existing } = useFuelUp(fuelId)
  const vehicleId = isEdit ? existing?.vehicleId ?? 0 : Number(params.id)
  const { data: vehicle } = useVehicle(vehicleId)
  const { data: log } = useFuel(vehicleId)
  const navigate = useNavigate()
  const qc = useQueryClient()

  const [nums, setNums] = useState<Record<Num, string>>({ total: '', ppl: '', liters: '' })
  const [order, setOrder] = useState<Num[]>(['total', 'ppl'])
  const [grade, setGrade] = useState<FuelGrade | null>(null)
  const gradeTouched = useRef(false)
  const [odometer, setOdometer] = useState('')
  const [fullTank, setFullTank] = useState(true)
  const [date, setDate] = useState(today())
  const [station, setStation] = useState('')
  const [scanned, setScanned] = useState<FuelScan | null>(null)
  const camera = useRef<HTMLInputElement>(null)
  const library = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!existing) return
    setNums({ total: show(existing.total, 'total'), ppl: show(existing.pricePerLiter, 'ppl'), liters: show(existing.liters, 'liters') })
    setGrade(existing.grade)
    gradeTouched.current = true
    setOdometer(existing.odometer?.toString() ?? '')
    setFullTank(existing.fullTank)
    setDate(existing.date)
    setStation(existing.station ?? '')
  }, [existing])

  // Remember the grade from the last fill-up of this car.
  useEffect(() => {
    if (!isEdit && !gradeTouched.current && log?.stats.lastGrade) setGrade(log.stats.lastGrade)
  }, [isEdit, log])

  const computed = computedOf(order)
  const edit = (field: Num) => (e: React.ChangeEvent<HTMLInputElement>) => {
    const nextOrder = [field, ...order.filter(f => f !== field)]
    setOrder(nextOrder)
    setNums(recompute({ ...nums, [field]: e.target.value }, computedOf(nextOrder)))
  }
  const pickGrade = (g: FuelGrade) => { gradeTouched.current = true; setGrade(g) }

  const scan = useMutation({
    mutationFn: scanFuel,
    onSuccess: s => {
      setScanned(s)
      setNums({ total: show(s.total, 'total'), ppl: show(s.pricePerLiter, 'ppl'), liters: show(s.liters, 'liters') })
      setOrder(['liters', 'ppl'])
      if (s.grade) pickGrade(s.grade)
      if (s.station && !station) setStation(s.station)
      if (s.date) setDate(s.date)
    },
  })
  const onPhoto = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (file) { setScanned(null); scan.mutate(file) }
  }

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['fuel'] })
    qc.invalidateQueries({ queryKey: keys.vehicles })
  }

  const save = useMutation({
    mutationFn: () => {
      const total = pos(nums.total), liters = pos(nums.liters), ppl = pos(nums.ppl)
      if (!total && !(liters && ppl)) throw new Error(t('Enter the total, or litres and price per litre.', 'Gib den Betrag ein – oder Liter und Literpreis.'))
      const body: FuelUpInput = {
        date: date || null, odometer: parseIntOrNull(odometer), grade, liters, pricePerLiter: ppl, total,
        fullTank, station: station.trim() || null, notes: existing?.notes ?? null,
      }
      return isEdit ? api.put<FuelUp>(`/fuel/${fuelId}`, body) : api.post<FuelUp>(`/vehicles/${vehicleId}/fuel`, body)
    },
    onSuccess: f => { refresh(); navigate(`/vehicles/${f.vehicleId}?tab=fuel`, { replace: true }) },
  })

  const remove = useMutation({
    mutationFn: () => api.del(`/fuel/${fuelId}`),
    onSuccess: () => { refresh(); navigate(`/vehicles/${vehicleId}?tab=fuel`, { replace: true }) },
  })

  if (isEdit && !existing) return <div className="center"><Spinner /></div>

  const lastOdo = log?.stats.lastOdometer ?? vehicle?.stats.latestOdometer ?? null
  const odo = parseIntOrNull(odometer)
  const since = !isEdit && odo != null && lastOdo != null ? odo - lastOdo : null

  const numField = (field: Num, label: string) => (
    <label className={`field fuel-num ${computed === field ? 'computed' : ''}`}>
      <span>{label}{computed === field && <em> · {t('auto', 'auto')}</em>}</span>
      <input inputMode="decimal" enterKeyHint="next" value={nums[field]} onChange={edit(field)} />
    </label>
  )

  return (
    <>
      <TopBar back title={`${isEdit ? t('Edit fill-up', 'Tanken bearbeiten') : t('Fuel', 'Tanken')}${vehicle ? ` · ${vehicle.name}` : ''}`} />
      <form className="form fuel-form" onSubmit={e => { e.preventDefault(); save.mutate() }}>
        <div className="scan-row">
          <button type="button" className="btn scan-btn" disabled={scan.isPending} onClick={() => camera.current?.click()}>
            {scan.isPending ? <><Spinner size={18} /> {t('Reading photo…', 'Foto wird gelesen…')}</> : <><Camera size={20} /> {t('Scan pump or receipt', 'Zapfsäule oder Beleg scannen')}</>}
          </button>
          <button type="button" className="btn" aria-label={t('Pick photo from library', 'Foto aus der Mediathek wählen')} disabled={scan.isPending}
            onClick={() => library.current?.click()}><Images size={20} /></button>
          <input ref={camera} type="file" accept="image/*" capture="environment" hidden onChange={onPhoto} />
          <input ref={library} type="file" accept="image/*,.heic,.heif" hidden onChange={onPhoto} />
        </div>
        <ErrorText error={scan.error} />
        {scanned && (
          <div className={`scan-note ${scanned.warnings.length ? 'warn' : ''}`}>
            {scanned.warnings.length ? <TriangleAlert size={16} /> : null}
            <span>
              {scanned.source === 'receipt' ? t('Read from the receipt', 'Vom Beleg gelesen') : t('Read from the pump display', 'Von der Zapfsäule gelesen')}
              {scanned.productName ? ` (${scanned.productName})` : ''}. {t('Check the numbers.', 'Prüf die Zahlen.')}
              {!scanned.grade && scanned.productName ? t(' Pick the grade below.', ' Wähl unten die Sorte.') : ''}
              {scanned.warnings.map(w => <span key={w} className="block">{w}</span>)}
            </span>
          </div>
        )}

        <div className="grade-chips" role="radiogroup" aria-label={t('Fuel grade', 'Kraftstoffsorte')}>
          {FUEL_GRADES.map(g => (
            <button key={g.value} type="button" role="radio" aria-checked={grade === g.value}
              className={`chip grade ${grade === g.value ? 'active' : ''}`} onClick={() => pickGrade(g.value)}>{g.label}</button>
          ))}
        </div>

        <div className="fuel-nums">
          {numField('total', t('Total €', 'Betrag €'))}
          {numField('ppl', t('€ / litre', '€ / Liter'))}
          {numField('liters', t('Litres', 'Liter'))}
        </div>

        <div className="grid-2">
          <Field label={t('Odometer (km)', 'km-Stand')}
            hint={since != null ? (since >= 0 ? t(`+${fmtNum(since)} km since last fill-up`, `+${fmtNum(since)} km seit dem letzten Tanken`) : t('Lower than the last reading', 'Weniger als beim letzten Mal')) : undefined}>
            <input inputMode="numeric" value={odometer} onChange={e => setOdometer(e.target.value)}
              placeholder={lastOdo != null ? `${t('last', 'zuletzt')} ${fmtKm(lastOdo)}` : 'optional'} />
          </Field>
          <label className="check" style={{ alignSelf: 'end' }}>
            <input type="checkbox" checked={fullTank} onChange={e => setFullTank(e.target.checked)} />
            {t('Full tank', 'Vollgetankt')}
          </label>
          <Field label={t('Date', 'Datum')}><input type="date" value={date} onChange={e => setDate(e.target.value)} /></Field>
          <Field label={t('Station', 'Tankstelle')}><input value={station} onChange={e => setStation(e.target.value)} placeholder="optional" /></Field>
        </div>
        {!fullTank && <p className="faint" style={{ margin: 0 }}>{t('Partial fill-ups count towards the consumption of the next full tank.', 'Teilfüllungen zählen zum Verbrauch der nächsten vollen Tankfüllung.')}</p>}

        <ErrorText error={save.error ?? remove.error} />
        <button className="btn btn-primary btn-big" disabled={save.isPending || scan.isPending}>
          {save.isPending ? <Spinner size={18} /> : isEdit ? t('Save', 'Speichern') : t('Save fill-up', 'Tanken speichern')}
        </button>
        {isEdit && (
          <ConfirmButton onConfirm={() => remove.mutate()} confirmLabel={t('Tap again to delete this fill-up', 'Nochmal tippen zum Löschen')}>{t('Delete fill-up', 'Tanken löschen')}</ConfirmButton>
        )}
      </form>
    </>
  )
}
