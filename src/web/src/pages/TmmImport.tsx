import { useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, FileSpreadsheet, Trash2 } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { importTmm, previewTmm, type TmmPreview, type TmmResult } from '../api'
import { ErrorText, Spinner, TopBar } from '../components/ui'
import { fmtDate, fmtMoney } from '../format'
import { keys, useVehicles } from '../hooks'
import { kindLabel, plural, t } from '../i18n'
import { pasteKey, usePasteFiles } from '../paste'

interface Item {
  key: string
  file: File
  preview?: TmmPreview
  error?: unknown
  /** 'new' = create the car from the export, else an existing car's id. */
  target: 'new' | number
  result?: TmmResult
}

const isCsv = (f: File) => /\.csv$/i.test(f.name) || f.type === 'text/csv'

export default function TmmImport() {
  const { data: vehicles = [] } = useVehicles()
  const qc = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const [items, setItems] = useState<Item[]>([])
  const [over, setOver] = useState(false)
  const [importing, setImporting] = useState(false)
  const [error, setError] = useState<unknown>(null)

  const update = (key: string, patch: Partial<Item>) => setItems(xs => xs.map(x => (x.key === key ? { ...x, ...patch } : x)))

  const add = (files: FileList | File[] | null) => {
    const list = Array.from(files ?? []).filter(isCsv)
    for (const file of list) {
      const key = `${file.name}-${file.size}-${Math.random()}`
      setItems(xs => [...xs, { key, file, target: 'new' }])
      previewTmm(file)
        .then(p => update(key, { preview: p, target: p.matchVehicleId ?? 'new' }))
        .catch(e => update(key, { error: e }))
    }
  }
  usePasteFiles(add, isCsv, !importing)

  const ready = items.filter(i => i.preview && !i.result)
  const loading = items.some(i => !i.preview && !i.error)

  const importAll = async () => {
    setImporting(true)
    setError(null)
    for (const item of ready) {
      try {
        const result = await importTmm(item.file, item.target === 'new' ? null : item.target)
        update(item.key, { result })
      } catch (e) {
        setError(e)
        break
      }
    }
    qc.invalidateQueries({ queryKey: keys.vehicles })
    qc.invalidateQueries({ queryKey: ['entries'] })
    qc.invalidateQueries({ queryKey: ['fuel'] })
    qc.invalidateQueries({ queryKey: keys.tags })
    setImporting(false)
  }

  return (
    <>
      <TopBar back="/" title={t('Import from Track My Mods', 'Import aus Track My Mods')} />

      <div className={`card dropzone ${over ? 'over' : ''}`}
        onDragOver={e => { e.preventDefault(); setOver(true) }}
        onDragLeave={() => setOver(false)}
        onDrop={e => { e.preventDefault(); setOver(false); add(e.dataTransfer.files) }}>
        <FileSpreadsheet size={40} />
        <h2>{t('Add CSV exports', 'CSV-Exporte hinzufügen')}</h2>
        <p className="muted" style={{ margin: 0 }}>
          {t('In Track My Mods, export each car as CSV (one file per car). You see everything before it is imported.',
            'Exportier in Track My Mods jedes Auto als CSV (eine Datei pro Auto). Du siehst alles, bevor es importiert wird.')}
        </p>
        <button className="btn btn-primary" style={{ marginTop: 8 }} disabled={importing} onClick={() => input.current?.click()}>
          {t('Choose files', 'Dateien wählen')}
        </button>
        <p className="faint hide-touch" style={{ margin: 0 }}>
          {t(`…or drop them here, or paste with ${pasteKey()}`, `…oder hierher ziehen bzw. mit ${pasteKey()} einfügen`)}
        </p>
        <input ref={input} type="file" hidden multiple accept=".csv,text/csv"
          onChange={e => { add(e.target.files); e.target.value = '' }} />
      </div>

      {items.map(item => (
        <FileCard key={item.key} item={item} vehicles={vehicles} disabled={importing}
          onTarget={target => update(item.key, { target })}
          onRemove={() => setItems(xs => xs.filter(x => x.key !== item.key))} />
      ))}

      {items.some(i => (i.preview?.fuelUps ?? 0) > 0) && (
        <p className="faint">
          {t('Track My Mods has no litres for fill-ups, so they count towards costs but not towards consumption.',
            'Track My Mods speichert beim Tanken keine Liter – die Tankfüllungen zählen zu den Kosten, aber nicht zum Verbrauch.')}
        </p>
      )}

      <ErrorText error={error} />
      {(ready.length > 0 || loading) && (
        <div className="form-actions">
          <button className="btn btn-primary" disabled={importing || loading || ready.length === 0} onClick={importAll}>
            {importing || loading ? <Spinner size={18} /> : ready.length === 1
              ? t('Import', 'Importieren')
              : t(`Import ${ready.length} cars`, `${ready.length} Autos importieren`)}
          </button>
        </div>
      )}
    </>
  )
}

function FileCard({ item, vehicles, disabled, onTarget, onRemove }: {
  item: Item; vehicles: { id: number; name: string }[]; disabled: boolean
  onTarget: (t: 'new' | number) => void; onRemove: () => void
}) {
  const p = item.preview
  const v = p?.vehicle
  const counts = p && [
    p.mods && plural(p.mods, ['mod', 'Mod'], ['mods', 'Mods']),
    p.maintenance && `${p.maintenance}× ${kindLabel('Maintenance')}`,
    p.expenses && plural(p.expenses, ['expense', 'Ausgabe'], ['expenses', 'Ausgaben']),
    p.fuelUps && plural(p.fuelUps, ['fill-up', 'Tankfüllung'], ['fill-ups', 'Tankfüllungen']),
  ].filter(Boolean).join(' · ')
  // Duplicates were checked against the matched car; only meaningful while that one is the target.
  const duplicates = p && p.matchVehicleId != null && item.target === p.matchVehicleId ? p.duplicates : 0

  return (
    <div className="card card-pad stack" style={{ marginTop: 12, gap: 10 }}>
      <div className="row" style={{ alignItems: 'flex-start' }}>
        <div className="grow" style={{ minWidth: 0 }}>
          <h2 style={{ overflowWrap: 'anywhere' }}>{v?.name ?? item.file.name}</h2>
          <div className="faint">
            {v ? [[v.year, v.make, v.model, v.badge].filter(Boolean).join(' '), v.plate].filter(Boolean).join(' · ') : item.file.name}
          </div>
        </div>
        {!item.result && <button className="icon-btn" aria-label={t('Remove', 'Entfernen')} disabled={disabled} onClick={onRemove}><Trash2 size={18} /></button>}
      </div>

      {!p && !item.error && <Spinner />}
      <ErrorText error={item.error} />

      {p && !item.result && (
        <>
          <div className="row">
            <span className="grow">{counts || t('No entries', 'Keine Einträge')}{p.planned ? ` · ${p.planned} ${t('planned', 'geplant')}` : ''}</span>
            <strong>{fmtMoney(p.total)}</strong>
          </div>
          {v?.purchasePrice != null && (
            <div className="faint">{t('Bought', 'Gekauft')} {fmtDate(v.purchaseDate)} · {fmtMoney(v.purchasePrice)}</div>
          )}
          <label className="field">
            <span>{t('Import into', 'Importieren in')}</span>
            <select value={item.target} disabled={disabled}
              onChange={e => onTarget(e.target.value === 'new' ? 'new' : Number(e.target.value))}>
              <option value="new">{t(`New car “${v!.name}”`, `Neues Auto „${v!.name}“`)}</option>
              {vehicles.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </label>
          {duplicates > 0 && (
            <div className="notice">
              {t(`${duplicates} of these are already on this car and will be skipped.`,
                `${duplicates} davon sind schon bei diesem Auto und werden übersprungen.`)}
            </div>
          )}
          {p.warnings.map(w => <div key={w} className="notice warn"><AlertTriangle size={18} /> {w}</div>)}
          {p.items.length > 0 && (
            <details>
              <summary className="faint" style={{ cursor: 'pointer' }}>{t('Show all', 'Alle anzeigen')} ({p.items.length})</summary>
              <div className="list" style={{ marginTop: 6 }}>
                {p.items.map((x, i) => (
                  <div key={i} className="list-item" style={{ minHeight: 44, padding: '6px 0', opacity: duplicates && x.duplicate ? 0.45 : 1 }}>
                    <div className="main">
                      <div className="t" style={{ whiteSpace: 'normal' }}>{x.title || t('Fill-up', 'Tanken')}</div>
                      <div className="s">{[x.type === 'Fuel' ? t('Fuel', 'Tanken') : kindLabel(x.type),
                        x.status === 'Planned' ? t('planned', 'geplant') : null, x.date ? fmtDate(x.date) : null].filter(Boolean).join(' · ')}</div>
                    </div>
                    <div className="amount">{fmtMoney(x.cost)}</div>
                  </div>
                ))}
              </div>
            </details>
          )}
        </>
      )}

      {item.result && (
        <div className="row">
          <CheckCircle2 size={20} color="var(--ok)" />
          <span className="grow">
            {t(`Imported: ${item.result.entries} entries, ${item.result.fuelUps} fill-ups`,
              `Importiert: ${item.result.entries} Einträge, ${item.result.fuelUps} Tankfüllungen`)}
            {item.result.skipped ? t(` (${item.result.skipped} skipped)`, ` (${item.result.skipped} übersprungen)`) : ''}
          </span>
          <Link to={`/vehicles/${item.result.vehicleId}`} className="btn">{t('Open', 'Öffnen')}</Link>
        </div>
      )}
    </div>
  )
}
