// Pure logic for turning a reviewed bill into entries. No React here, so it's easy to test.
import type { BillExtraction, EntryKind, EntryStatus, ImportEntry, LineType, Purpose } from './api'
import { parseDecimal, toInput } from './format'
import { t } from './i18n'

export type SplitMode = 'single' | 'category' | 'item'

export interface ReviewLine {
  key: string
  include: boolean
  name: string
  /** Concise name from the AI ("55Parts Offene Ansaugung"), used for entry titles. */
  shortName: string
  brand: string
  partNumber: string
  quantity: string
  unitPrice: string
  lineType: LineType
  category: string | null
  purpose: Purpose
  /** Shop that sold this line on a marketplace order with several sellers; '' for single-shop bills. */
  seller: string
}

export interface ReviewState {
  vehicleId: number | null
  date: string
  vendor: string
  odometer: string
  status: EntryStatus
  mode: SplitMode
  /** Shipping & fees spread over the entries (else they become their own entry). Discounts are always spread. */
  spreadExtras: boolean
  tags: string[]
  notes: string
  lines: ReviewLine[]
  /** Per-group overrides, keyed by group key. */
  titles: Record<string, string>
  kinds: Record<string, EntryKind>
}

export interface Group {
  key: string
  lines: ReviewLine[]
  defaultTitle: string
  title: string
  kind: EntryKind
  tags: string[]
  /** Share of shipping/fees added to this group (0 if not spread). */
  extraShare: number
  /** Share of discounts taken off this group (negative or 0). */
  discountShare: number
  /** Shop for the entry: the seller when all its lines come from one, else null (→ the bill's shop). */
  vendor: string | null
  total: number
}

const EXTRA_TYPES: LineType[] = ['shipping', 'fee', 'discount']
export const isExtra = (l: ReviewLine) => EXTRA_TYPES.includes(l.lineType)
const isDiscount = (l: ReviewLine) => l.lineType === 'discount' || lineTotal(l) < 0

/** Each item → its own entry with its own price, shipping as a separate entry. Bigger groups spread shipping. */
export const spreadDefault = (mode: SplitMode) => mode !== 'item'

export const kindFromPurpose = (p: Purpose): EntryKind =>
  p === 'upgrade' ? 'Mod' : p === 'other' ? 'Expense' : 'Maintenance'

const round2 = (n: number) => Math.round(n * 100) / 100
const num = (s: string, fallback: number) => {
  const n = parseDecimal(s)
  return n == null || Number.isNaN(n) ? fallback : n
}
export const lineTotal = (l: ReviewLine) => round2(num(l.quantity, 1) * num(l.unitPrice, 0))

const tagify = (s: string) => s.trim().toLowerCase().split(/\s+/).filter(Boolean).join('_')

/** Bill → editable review state with sensible defaults. */
export function initialState(x: BillExtraction | null, vehicleId: number | null): ReviewState {
  const lines: ReviewLine[] = (x?.line_items ?? []).map((l, i) => {
    const qty = l.quantity || 1
    // If the bill's line total doesn't equal qty × unit (per-line discount), trust the total.
    const unit = Math.abs(qty * l.unit_price - l.total) > 0.01 ? l.total / qty : l.unit_price
    return {
      key: `l${i}`, include: true, name: l.name, shortName: l.short_name?.trim() || '', brand: l.brand ?? '', partNumber: l.part_number ?? '',
      quantity: toInput(qty), unitPrice: toInput(unit, 2),
      lineType: l.line_type, category: l.category, purpose: l.purpose ?? 'repair', seller: l.seller?.trim() || '',
    }
  })
  const notes = [x?.invoice_number ? `${t('Invoice', 'Rechnung')} ${x.invoice_number}` : null, x?.notes].filter(Boolean).join(' · ')
  return {
    vehicleId, date: x?.invoice_date ?? '', vendor: x?.vendor ?? '', odometer: x?.odometer_km?.toString() ?? '',
    status: 'Done', mode: 'item', spreadExtras: spreadDefault('item'), tags: [], notes, lines, titles: {}, kinds: {},
  }
}

function groupKey(mode: SplitMode, l: ReviewLine) {
  if (mode === 'single') return 'all'
  if (mode === 'item') return l.key
  return `${l.category ?? 'misc'}|${kindFromPurpose(l.purpose)}`
}

const titleOf = (l: ReviewLine) => l.shortName || l.name

function defaultTitle(lines: ReviewLine[], vendor: string) {
  if (lines.length === 0) return vendor ? t(`${vendor} order`, `Bestellung ${vendor}`) : t('Bill', 'Rechnung')
  const sorted = [...lines].sort((a, b) => lineTotal(b) - lineTotal(a))
  if (lines.length === 1) return titleOf(sorted[0])
  if (lines.length === 2) return `${titleOf(sorted[0])} + ${titleOf(sorted[1])}`
  return `${titleOf(sorted[0])} + ${lines.length - 1} ${t('more', 'weitere')}`
}

const sum = (ls: ReviewLine[]) => round2(ls.reduce((a, l) => a + lineTotal(l), 0))

/** The one seller of these lines, or null if they come from none or several. */
const sellerOf = (lines: ReviewLine[]) => {
  const all = new Set(lines.map(l => l.seller))
  return all.size === 1 && !all.has('') ? [...all][0] : null
}

/** Lines per seller ('' = the bill's own shop), in order of appearance. */
function bySeller(lines: ReviewLine[]) {
  const map = new Map<string, ReviewLine[]>()
  for (const l of lines) map.set(l.seller, [...(map.get(l.seller) ?? []), l])
  return map
}

/**
 * Adds `amount` to the groups in proportion to `weight` (a seller's items in each group); the last one takes the
 * rounding rest. Groups with no weight get nothing, unless no group has any — then it goes by group totals.
 */
function spread(groups: Group[], amount: number, field: 'extraShare' | 'discountShare', weight: (g: Group) => number) {
  let targets = groups.map(g => ({ g, w: weight(g) })).filter(x => x.w > 0)
  if (targets.length === 0) targets = groups.map(g => ({ g, w: g.total }))
  if (targets.length === 0) return
  const base = targets.reduce((a, x) => a + x.w, 0)
  let assigned = 0
  targets.forEach(({ g, w }, i) => {
    const share = i === targets.length - 1
      ? round2(amount - assigned)
      : round2(base > 0 ? amount * (w / base) : amount / targets.length)
    assigned = round2(assigned + share)
    g[field] = round2(g[field] + share)
    g.total = round2(g.total + share)
  })
}

/**
 * Groups the included lines into future entries. Discounts are always spread proportionally (they lower what
 * each part really cost); shipping & fees are spread too, or become one entry of their own.
 */
export function buildGroups(s: ReviewState): Group[] {
  const included = s.lines.filter(l => l.include)
  let items = included.filter(l => !isExtra(l))
  let extras = included.filter(isExtra)
  // Only extras (e.g. a pure service fee bill) or single mode → everything is a normal line.
  if (items.length === 0 || s.mode === 'single') { items = included; extras = [] }

  const map = new Map<string, ReviewLine[]>()
  for (const l of items) {
    const k = groupKey(s.mode, l)
    map.set(k, [...(map.get(k) ?? []), l])
  }

  const groups: Group[] = [...map.entries()].map(([key, lines]) => {
    const top = [...lines].sort((a, b) => lineTotal(b) - lineTotal(a))[0]
    const def = defaultTitle(lines, s.vendor)
    const cats = [...new Set(lines.map(l => l.category).filter((c): c is string => !!c).map(tagify))]
    return {
      key, lines, defaultTitle: def, title: s.titles[key] ?? def,
      kind: s.kinds[key] ?? kindFromPurpose(top.purpose),
      tags: [...new Set([...s.tags, ...cats])],
      extraShare: 0, discountShare: 0, vendor: s.mode === 'single' ? null : sellerOf(lines),
      total: sum(lines),
    }
  })
  const itemGroups = [...groups]
  // A seller's shipping/discount belongs to that seller's items (marketplace orders: one block per shop).
  const sellerWeight = (seller: string) => (g: Group) => sum(g.lines.filter(l => l.seller === seller))

  // Shipping first: when it's spread, a discount is then shared out over item + its shipping share.
  for (const [seller, charges] of bySeller(extras.filter(l => !isDiscount(l)))) {
    if (s.spreadExtras) { spread(itemGroups, sum(charges), 'extraShare', sellerWeight(seller)); continue }
    const key = seller ? `extras|${seller}` : 'extras'
    const def = charges.length === 1 && charges[0].lineType === 'shipping'
      ? t('Shipping', 'Versand') : t('Shipping & fees', 'Versand & Gebühren')
    const shop = seller || s.vendor.trim()
    const title = shop ? `${def} ${shop}` : def
    groups.push({
      key, lines: charges, defaultTitle: title, title: s.titles[key] ?? title,
      kind: s.kinds[key] ?? 'Expense', tags: [...s.tags], extraShare: 0, discountShare: 0, vendor: seller || null, total: sum(charges),
    })
  }
  // Vouchers lower what the parts cost; a separate shipping entry keeps its price from the bill.
  for (const [seller, discounts] of bySeller(extras.filter(isDiscount))) {
    spread(itemGroups, sum(discounts), 'discountShare', sellerWeight(seller))
  }
  return groups
}

export function buildImport(s: ReviewState): ImportEntry[] {
  if (!s.vehicleId) throw new Error(t('Choose a car first.', 'Wähl zuerst ein Auto.'))
  const odometer = parseDecimal(s.odometer)
  return buildGroups(s).map(g => {
    const lineItems = g.lines.map(l => ({
      name: l.name.trim() || t('Item', 'Position'), brand: l.brand.trim() || null, partNumber: l.partNumber.trim() || null,
      quantity: num(l.quantity, 1), unitPrice: num(l.unitPrice, 0),
    }))
    if (g.extraShare !== 0) {
      lineItems.push({ name: t('Shipping & fees (share)', 'Versand & Gebühren (Anteil)'), brand: null, partNumber: null, quantity: 1, unitPrice: g.extraShare })
    }
    if (g.discountShare !== 0) {
      lineItems.push({ name: t('Discount (share)', 'Rabatt (Anteil)'), brand: null, partNumber: null, quantity: 1, unitPrice: g.discountShare })
    }
    return {
      vehicleId: s.vehicleId!,
      entry: {
        kind: g.kind, status: s.status, title: (g.title.trim() || g.defaultTitle).slice(0, 200),
        date: s.date || null, endDate: null,
        odometer: odometer == null || Number.isNaN(odometer) ? null : Math.round(odometer),
        vendor: g.vendor ?? (s.vendor.trim() || null), notes: s.notes.trim() || null,
        manualTotal: null, relatedEntryId: null, tags: g.tags, lineItems,
      },
    }
  })
}
