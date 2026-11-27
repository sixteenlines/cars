// Thin typed client for the ASP.NET Core API. Cookies carry the session (same origin).
import { getLang, t } from './i18n'

export type EntryKind = 'Mod' | 'Maintenance' | 'Expense' | 'Event'
export type EntryStatus = 'Planned' | 'Done'

export interface Me { id: string; email: string; displayName: string; isAdmin: boolean; language: 'en' | 'de' | null }

export interface VehicleStats {
  totalSpent: number; modsTotal: number; maintenanceTotal: number; expensesTotal: number; plannedTotal: number
  modCount: number; entryCount: number; plannedCount: number; lastMaintenanceDate: string | null
  latestOdometer: number | null; photoCount: number
  fuelTotal: number; fuelCount: number; lastFuelDate: string | null; avgConsumption: number | null
  eventsTotal: number; eventCount: number
}

export interface Vehicle {
  id: number; name: string; make: string | null; model: string | null; year: number | null; badge: string | null
  colorExterior: string | null; colorInterior: string | null; plate: string | null; vin: string | null
  purchasePrice: number | null; purchaseDate: string | null; purchaseOdometer: number | null
  currentOdometer: number | null; includePurchaseInTotals: boolean; notes: string | null
  coverPhotoId: number | null; stats: VehicleStats
}
export type VehicleInput = Omit<Vehicle, 'id' | 'stats'>

export interface LineItem {
  id?: number | null; name: string; brand: string | null; partNumber: string | null
  quantity: number; unitPrice: number; total?: number
}

export interface Photo {
  id: number; vehicleId: number; entryId: number | null; width: number; height: number
  takenAt: string | null; uploadedAt: string; caption: string | null
}

export interface EntryRef { id: number; title: string }

export interface Entry {
  id: number; vehicleId: number; kind: EntryKind; status: EntryStatus; title: string
  date: string | null; endDate: string | null; odometer: number | null; vendor: string | null; location: string | null; notes: string | null
  currency: string; manualTotal: number | null; total: number
  relatedEntry: EntryRef | null; linkedEntries: EntryRef[]; tags: string[]; lineItems: LineItem[]; photos: Photo[]
  documents: { id: number; fileName: string; contentType: string; vendor: string | null; invoiceDate: string | null }[]
  createdAt: string; updatedAt: string
}

export interface EntryInput {
  kind: EntryKind; status: EntryStatus; title: string; date: string | null; endDate: string | null
  odometer: number | null; vendor: string | null; notes: string | null; manualTotal: number | null
  relatedEntryId: number | null; tags: string[]; lineItems: Omit<LineItem, 'id' | 'total'>[]; location?: string | null
}

export interface TagCount { name: string; count: number }

export interface Invite {
  id: number; note: string | null; grantsAdmin: boolean; createdAt: string; expiresAt: string
  usedAt: string | null; usedBy: string | null
}
export interface AdminUser { id: string; email: string; displayName: string; isAdmin: boolean; createdAt: string; vehicleCount: number }

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) { super(message); this.status = status }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  // Accept-Language: the server answers errors and warnings in the app's language.
  const init: RequestInit = { method, credentials: 'same-origin', headers: { 'Accept-Language': getLang() } }
  if (body instanceof FormData) {
    init.body = body
  } else if (body !== undefined) {
    init.body = JSON.stringify(body)
    ;(init.headers as Record<string, string>)['Content-Type'] = 'application/json'
  }
  const res = await fetch(`/api${path}`, init)
  if (!res.ok) {
    let message = res.statusText || t(`Request failed (${res.status})`, `Anfrage fehlgeschlagen (${res.status})`)
    try {
      const problem = await res.json()
      message = problem.detail ?? problem.title ?? message
    } catch { /* not JSON */ }
    if (res.status === 429) message = t('Too many attempts. Please wait a minute.', 'Zu viele Versuche. Warte bitte eine Minute.')
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
  del: (path: string) => request<void>('DELETE', path),
}

export const photoUrl = (id: number, size: 'thumb' | 'full' = 'thumb') => `/api/photos/${id}/${size}`

export async function uploadPhotos(vehicleId: number, files: File[], entryId?: number | null) {
  // One request per file: keeps each request small (Cloudflare limit) and lets us show progress per photo.
  const photos: Photo[] = []
  const failed: string[] = []
  for (const file of files) {
    const form = new FormData()
    form.append('files', file)
    if (entryId) form.append('entryId', String(entryId))
    try {
      const res = await api.post<{ photos: Photo[]; failed: string[] }>(`/vehicles/${vehicleId}/photos`, form)
      photos.push(...res.photos)
      failed.push(...res.failed)
    } catch {
      failed.push(file.name)
    }
  }
  return { photos, failed }
}

// ---- Bill import -------------------------------------------------------------------------------

export type DocumentStatus = 'Processing' | 'Ready' | 'Failed' | 'Imported'
export type LineType = 'part' | 'labour' | 'shipping' | 'discount' | 'fee' | 'other'
export type Purpose = 'upgrade' | 'repair' | 'service' | 'other'

export interface BillLine {
  name: string; short_name?: string | null; brand: string | null; part_number: string | null; quantity: number
  unit_price: number; total: number; line_type: LineType; category: string | null; purpose: Purpose
}

export interface BillExtraction {
  is_invoice: boolean; vendor: string | null; invoice_number: string | null; invoice_date: string | null
  currency: string | null; line_items: BillLine[]; grand_total: number | null; vehicle_hint: string | null
  odometer_km: number | null; notes: string | null
}

export interface BillDocument {
  id: number; vehicleId: number | null; fileName: string; contentType: string; sizeBytes: number
  status: DocumentStatus; error: string | null; extraction: BillExtraction | null; warnings: string[]
  possibleDuplicates: { id: number; fileName: string; createdAt: string; importedAt: string | null }[]
  entries: EntryRef[]; createdAt: string; importedAt: string | null; alreadyUploaded: boolean
}

export interface BillListItem {
  id: number; vehicleId: number | null; fileName: string; status: DocumentStatus; vendor: string | null
  invoiceDate: string | null; total: number | null; createdAt: string
}

export interface ImportEntry { vehicleId: number; entry: EntryInput }

export interface Usage {
  userId: string; displayName: string; importsThisMonth: number; costThisMonthUsd: number; importsTotal: number; costTotalUsd: number
  fuelScansThisMonth: number; fuelScansTotal: number
}

export const documentFileUrl = (id: number) => `/api/documents/${id}/file`

export function uploadDocument(file: File, vehicleId?: number | null) {
  const form = new FormData()
  form.append('file', file)
  if (vehicleId) form.append('vehicleId', String(vehicleId))
  return api.post<BillDocument>('/documents', form)
}

// ---- Fuel ----------------------------------------------------------------------------------------

export type FuelGrade = '95' | '98' | '100' | '102' | 'diesel'
export const FUEL_GRADES: { value: FuelGrade; label: string }[] = [
  { value: '95', label: '95' }, { value: '98', label: '98' }, { value: '100', label: '100' },
  { value: '102', label: '102' }, { value: 'diesel', label: 'Diesel' },
]
export const gradeLabel = (g: string | null) => (g == null ? '' : g === 'diesel' ? 'Diesel' : t(`RON ${g}`, `ROZ ${g}`))

export interface FuelUp {
  id: number; vehicleId: number; date: string; odometer: number | null; grade: FuelGrade | null
  liters: number | null; pricePerLiter: number | null; total: number; fullTank: boolean
  station: string | null; notes: string | null
  /** L/100 km of the full-to-full stretch this fill-up closes. */
  consumption: number | null; distanceKm: number | null; consumptionPlausible: boolean
}

export interface FuelUpInput {
  date: string | null; odometer: number | null; grade: FuelGrade | null; liters: number | null
  pricePerLiter: number | null; total: number | null; fullTank: boolean; station: string | null; notes: string | null
}

export interface FuelStats {
  count: number; totalCost: number; totalLiters: number; avgConsumption: number | null; costPer100Km: number | null
  distanceKm: number; thisMonthCost: number; thisYearCost: number; lastGrade: FuelGrade | null
  lastDate: string | null; lastOdometer: number | null; avgPricePerLiter: Record<string, number>
}

export interface FuelLog { fuelUps: FuelUp[]; stats: FuelStats }

export interface FuelScan {
  source: 'pump_display' | 'receipt' | 'other'; total: number | null; liters: number | null; pricePerLiter: number | null
  grade: FuelGrade | null; productName: string | null; station: string | null; date: string | null; warnings: string[]
}

export function scanFuel(file: File) {
  const form = new FormData()
  form.append('file', file)
  return api.post<FuelScan>('/fuel/scan', form)
}

// ---- Track My Mods import --------------------------------------------------------------------------

export interface TmmPreview {
  fileName: string
  vehicle: {
    name: string; make: string | null; model: string | null; year: number | null; badge: string | null
    plate: string | null; vin: string | null; purchasePrice: number | null; purchaseDate: string | null; purchaseOdometer: number | null
  }
  matchVehicleId: number | null
  mods: number; maintenance: number; expenses: number; events: number; planned: number; fuelUps: number; fuelUpsWithoutLiters: number; duplicates: number; total: number
  warnings: string[]
  items: { type: EntryKind | 'Fuel'; status: EntryStatus; title: string; date: string | null; cost: number; duplicate: boolean }[]
}
export interface TmmResult { vehicleId: number; vehicleName: string; entries: number; fuelUps: number; skipped: number }

export function previewTmm(file: File) {
  const form = new FormData()
  form.append('file', file)
  return api.post<TmmPreview>('/import/tmm/preview', form)
}

export function importTmm(file: File, vehicleId: number | null) {
  const form = new FormData()
  form.append('file', file)
  if (vehicleId) form.append('vehicleId', String(vehicleId))
  return api.post<TmmResult>('/import/tmm', form)
}
