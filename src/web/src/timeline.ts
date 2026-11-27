// Pure logic for the vehicle timeline: entries, fill-ups and photos merged by date. No React here.
import type { Entry, FuelUp, Photo, Vehicle } from './api'

export type TimelineItem =
  | { type: 'entry'; date: string; entry: Entry }
  /** All fill-ups of one month in one row, so tanking doesn't drown everything else. */
  | { type: 'fuel'; date: string; fuelUps: FuelUp[]; total: number; liters: number; avgConsumption: number | null }
  /** Photos not attached to an entry, one row per day. */
  | { type: 'photos'; date: string; photos: Photo[] }
  | { type: 'km'; date: string; km: number }
  | { type: 'purchase'; date: string; price: number | null; odometer: number | null }

export interface TimelineMonth { key: string; month: number; items: TimelineItem[] }
export interface TimelineYear { year: number; months: TimelineMonth[]; spent: number }
export interface Timeline {
  years: TimelineYear[]
  /** Planned entries without a date, shown on top. */
  undatedPlanned: Entry[]
  /** Done entries without a date, shown at the bottom. */
  undated: Entry[]
}

export interface TimelineFilter { entries: boolean; fuel: boolean; photos: boolean }

/** Milestone every 10.000 km. */
export const KM_STEP = 10_000

const round2 = (n: number) => Math.round(n * 100) / 100

/** Local calendar day of a timestamp ("2026-03-13T22:30:00Z" in Berlin → "2026-03-13"). */
export function localDay(iso: string) {
  if (/^\d{4}-\d{2}-\d{2}$/.test(iso)) return iso
  const d = new Date(iso)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const photoDay = (p: Photo) => localDay(p.takenAt ?? p.uploadedAt)

/** Dates where the odometer first passed each multiple of KM_STEP, from all readings that have a date. */
export function kmMilestones(v: Pick<Vehicle, 'purchaseDate' | 'purchaseOdometer'>, entries: Entry[], fuelUps: FuelUp[]) {
  const readings = [
    ...entries.filter(e => e.date && e.odometer != null && e.status === 'Done').map(e => ({ date: e.date!, km: e.odometer! })),
    ...fuelUps.filter(f => f.odometer != null).map(f => ({ date: f.date, km: f.odometer! })),
  ].sort((a, b) => a.date.localeCompare(b.date) || a.km - b.km)
  // Count from the purchase (or the first reading): a car bought at 13.525 km first "reaches" 20.000 km.
  let passed = Math.floor((v.purchaseOdometer ?? readings[0]?.km ?? 0) / KM_STEP)
  // A typo far above the later readings (270.000 instead of 27.000) would fire a run of milestones at once:
  // a reading only counts as far as every later reading confirms it.
  const confirmed = readings.map(r => r.km)
  for (let i = confirmed.length - 2; i >= 0; i--) confirmed[i] = Math.min(confirmed[i], confirmed[i + 1])
  const out: { date: string; km: number }[] = []
  readings.forEach((r, i) => {
    const reached = Math.floor(confirmed[i] / KM_STEP)
    // One reading after a long gap can pass several steps; only the highest one says something.
    if (reached > passed) out.push({ date: r.date, km: reached * KM_STEP })
    passed = Math.max(passed, reached)
  })
  return out
}

export function buildTimeline(vehicle: Vehicle, entries: Entry[], fuelUps: FuelUp[], photos: Photo[], filter: TimelineFilter): Timeline {
  const items: TimelineItem[] = []
  const undatedPlanned: Entry[] = []
  const undated: Entry[] = []

  if (filter.entries) {
    for (const e of entries) {
      if (e.date) items.push({ type: 'entry', date: e.date, entry: e })
      else (e.status === 'Planned' ? undatedPlanned : undated).push(e)
    }
  }

  if (filter.fuel) {
    const byMonth = new Map<string, FuelUp[]>()
    for (const f of fuelUps) byMonth.set(f.date.slice(0, 7), [...(byMonth.get(f.date.slice(0, 7)) ?? []), f])
    for (const list of byMonth.values()) {
      const sorted = [...list].sort((a, b) => b.date.localeCompare(a.date))
      const withC = list.filter(f => f.consumption != null && f.consumptionPlausible && f.distanceKm)
      const dist = withC.reduce((a, f) => a + f.distanceKm!, 0)
      items.push({
        type: 'fuel', date: sorted[0].date, fuelUps: sorted,
        total: round2(list.reduce((a, f) => a + f.total, 0)),
        liters: round2(list.reduce((a, f) => a + (f.liters ?? 0), 0)),
        // Distance-weighted, like the overall average.
        avgConsumption: dist > 0 ? withC.reduce((a, f) => a + f.consumption! * f.distanceKm!, 0) / dist : null,
      })
    }
  }

  if (filter.photos) {
    const byDay = new Map<string, Photo[]>()
    for (const p of photos.filter(p => p.entryId == null)) byDay.set(photoDay(p), [...(byDay.get(photoDay(p)) ?? []), p])
    for (const [date, list] of byDay) items.push({ type: 'photos', date, photos: list.sort((a, b) => (a.takenAt ?? a.uploadedAt).localeCompare(b.takenAt ?? b.uploadedAt)) })
  }

  for (const m of kmMilestones(vehicle, entries, fuelUps)) items.push({ type: 'km', ...m })
  if (vehicle.purchaseDate) items.push({ type: 'purchase', date: vehicle.purchaseDate, price: vehicle.purchasePrice, odometer: vehicle.purchaseOdometer })

  // Newest first. On the same day: milestones above what reached them, the purchase at the very bottom.
  const rank: Record<TimelineItem['type'], number> = { km: 0, entry: 1, fuel: 2, photos: 3, purchase: 4 }
  items.sort((a, b) => b.date.localeCompare(a.date) || rank[a.type] - rank[b.type])

  const years: TimelineYear[] = []
  for (const it of items) {
    const year = Number(it.date.slice(0, 4))
    const month = Number(it.date.slice(5, 7))
    let y = years[years.length - 1]
    if (!y || y.year !== year) years.push(y = { year, months: [], spent: 0 })
    let m = y.months[y.months.length - 1]
    if (!m || m.month !== month) y.months.push(m = { key: `${year}-${month}`, month, items: [] })
    m.items.push(it)
    if (it.type === 'entry' && it.entry.status === 'Done') y.spent += it.entry.total
    if (it.type === 'fuel') y.spent += it.total
  }
  years.forEach(y => (y.spent = round2(y.spent)))
  return { years, undatedPlanned, undated }
}
