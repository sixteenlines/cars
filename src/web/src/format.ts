const money = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })
const moneyShort = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 })
const num = new Intl.NumberFormat('de-DE')
const date = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })

export const fmtMoney = (v: number | null | undefined) => (v == null ? '–' : money.format(v))
export const fmtMoneyShort = (v: number | null | undefined) =>
  v == null ? '–' : Math.abs(v) >= 1000 ? moneyShort.format(v) : money.format(v)
export const fmtKm = (v: number | null | undefined) => (v == null ? '–' : `${num.format(v)} km`)

/** "2026-03-13" → "13.03.2026" without timezone shifts. */
export function fmtDate(iso: string | null | undefined) {
  if (!iso) return '–'
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
  return date.format(new Date(y, m - 1, d))
}

export const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * Accepts German and English number styles: "1.234,56", "1234,56", "1,234.56", "1234.56", "1 234,56 €".
 * Returns null for empty input and NaN for garbage.
 */
export function parseDecimal(raw: string): number | null {
  let s = raw.replace(/[€\s ]/g, '')
  if (s === '') return null
  const lastComma = s.lastIndexOf(',')
  const lastDot = s.lastIndexOf('.')
  if (lastComma > lastDot) {
    s = s.replace(/\./g, '').replace(',', '.') // German: comma is decimal separator
  } else if (lastDot > lastComma && lastComma !== -1) {
    s = s.replace(/,/g, '') // English with thousands commas
  } else if (lastDot !== -1 && /^\d{1,3}(\.\d{3})+$/.test(s)) {
    s = s.replace(/\./g, '') // "1.400" → German thousands
  }
  const n = Number(s)
  return Number.isFinite(n) ? n : NaN
}

/** Number → editable string in German style ("1234,5"). */
export const toInput = (v: number | null | undefined) => (v == null ? '' : String(v).replace('.', ','))

export const parseIntOrNull = (raw: string) => {
  const n = parseDecimal(raw)
  return n == null || Number.isNaN(n) ? null : Math.round(n)
}
