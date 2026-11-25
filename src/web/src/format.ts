import { getLang, isDe } from './i18n'

// Formatters are built per language (the app remounts on a switch, so reading getLang() at call time is enough).
const locale = () => (getLang() === 'de' ? 'de-DE' : 'en-GB')
const cache = new Map<string, Intl.NumberFormat | Intl.DateTimeFormat>()
function nf(key: string, opts: Intl.NumberFormatOptions) {
  const k = `${locale()}|n|${key}`
  if (!cache.has(k)) cache.set(k, new Intl.NumberFormat(locale(), opts))
  return cache.get(k) as Intl.NumberFormat
}

export const fmtMoney = (v: number | null | undefined) =>
  v == null ? '–' : nf('money', { style: 'currency', currency: 'EUR' }).format(v)
export const fmtMoneyShort = (v: number | null | undefined) =>
  v == null ? '–' : Math.abs(v) >= 1000 ? nf('money0', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 }).format(v) : fmtMoney(v)
/** Fixed number of decimals, e.g. fmtNum(1.859, 3) → "1,859" (de) / "1.859" (en). */
export const fmtNum = (v: number, decimals = 0) =>
  nf(`f${decimals}`, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(v)
export const fmtKm = (v: number | null | undefined) => (v == null ? '–' : `${fmtNum(v)} km`)

/** "2026-03-13" → "13.03.2026" / "13/03/2026" without timezone shifts. */
export function fmtDate(iso: string | null | undefined) {
  if (!iso) return '–'
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
  const k = `${locale()}|d`
  if (!cache.has(k)) cache.set(k, new Intl.DateTimeFormat(locale(), { day: '2-digit', month: '2-digit', year: 'numeric' }))
  return (cache.get(k) as Intl.DateTimeFormat).format(new Date(y, m - 1, d))
}

export const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * Accepts German and English number styles: "1.234,56", "1234,56", "1,234.56", "1234.56", "1 234,56 €".
 * Ambiguous ones ("1.400", "1,400") follow the app language: thousands separator in that language's style.
 * Returns null for empty input and NaN for garbage.
 */
export function parseDecimal(raw: string): number | null {
  let s = raw.replace(/[€\s ]/g, '')
  if (s === '') return null
  const lastComma = s.lastIndexOf(',')
  const lastDot = s.lastIndexOf('.')
  if (lastComma !== -1 && lastDot !== -1) {
    // Both present: the last one is the decimal separator.
    s = lastComma > lastDot ? s.replace(/\./g, '').replace(',', '.') : s.replace(/,/g, '')
  } else if (lastComma !== -1) {
    s = !isDe() && /^\d{1,3}(,\d{3})+$/.test(s) ? s.replace(/,/g, '') : s.replace(/,(?=.*,)/g, '').replace(',', '.')
  } else if (lastDot !== -1 && isDe() && /^\d{1,3}(\.\d{3})+$/.test(s)) {
    s = s.replace(/\./g, '') // "1.400" → German thousands
  }
  const n = Number(s)
  return Number.isFinite(n) ? n : NaN
}

/** Number → editable string in the app language's style ("1234,5" / "1234.5"), optionally with fixed decimals. */
export const toInput = (v: number | null | undefined, decimals?: number) => {
  if (v == null) return ''
  const s = decimals == null ? String(v) : v.toFixed(decimals)
  return isDe() ? s.replace('.', ',') : s
}

export const parseIntOrNull = (raw: string) => {
  const n = parseDecimal(raw)
  return n == null || Number.isNaN(n) ? null : Math.round(n)
}
