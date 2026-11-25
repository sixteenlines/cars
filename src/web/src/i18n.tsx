// Two languages, inline: t('Save', 'Speichern'). Same idea as Loc.T on the server — every string keeps
// its translation right next to it, so nothing can go missing. German uses "du".
//
// Which language: logged in → the account setting, "automatic" = phone/browser language.
// Logged out (login, register) → the DE/EN switch on those pages, else the phone/browser language.
// Changing it remounts the app (see LangProvider), so plain t() calls during render are always current.
import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { EntryKind } from './api'

export type Lang = 'en' | 'de'

const STORAGE_KEY = 'ct_lang'

export const browserLang = (): Lang => ((navigator.languages?.[0] ?? navigator.language ?? 'en').toLowerCase().startsWith('de') ? 'de' : 'en')

export function storedLang(): Lang | null {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    return v === 'de' || v === 'en' ? v : null
  } catch { return null }
}

export function storeLang(lang: Lang | null) {
  try {
    if (lang) localStorage.setItem(STORAGE_KEY, lang)
    else localStorage.removeItem(STORAGE_KEY)
  } catch { /* private mode */ }
}

let current: Lang = storedLang() ?? browserLang()
document.documentElement.lang = current

/** Current language, also for non-React code (api.ts header, number formats). */
export const getLang = () => current
export const isDe = () => current === 'de'
export const t = (en: string, de: string) => (current === 'de' ? de : en)

const Ctx = createContext<{ lang: Lang; setAccountLang: (l: Lang | null) => void; setLocalLang: (l: Lang) => void }>({
  lang: current, setAccountLang: () => {}, setLocalLang: () => {},
})

/**
 * `accountLang` = the user's saved setting (null = automatic; undefined while unknown / logged out).
 * Children are keyed by language, so a switch re-renders everything with the new texts and formats.
 */
export function LangProvider({ accountLang, onLangChange, children }: {
  accountLang: Lang | null | undefined; onLangChange?: () => void; children: React.ReactNode
}) {
  const [local, setLocal] = useState<Lang | null>(storedLang())
  const lang: Lang = accountLang !== undefined ? accountLang ?? browserLang() : local ?? browserLang()
  current = lang
  const first = useRef(true)

  // Mirror the account's choice in this browser, so the next start (before /me answers) is already right.
  useEffect(() => { if (accountLang !== undefined && accountLang !== local) { storeLang(accountLang); setLocal(accountLang) } }, [accountLang, local])

  useEffect(() => {
    document.documentElement.lang = lang
    if (first.current) { first.current = false; return }
    onLangChange?.()   // e.g. refetch texts the server wrote (warnings, errors)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lang])

  const value = useMemo(() => ({
    lang,
    setAccountLang: (l: Lang | null) => { storeLang(l); setLocal(l) },
    setLocalLang: (l: Lang) => { storeLang(l); setLocal(l) },
  }), [lang])

  return <Ctx.Provider value={value}><div key={lang} style={{ display: 'contents' }}>{children}</div></Ctx.Provider>
}

export const useLang = () => useContext(Ctx)

/** Small DE | EN switch for pages without an account (login, register). */
export function LangSwitch() {
  const { lang, setLocalLang } = useLang()
  return (
    <div className="lang-switch" role="radiogroup" aria-label="Language / Sprache">
      {(['de', 'en'] as const).map(l => (
        <button key={l} type="button" role="radio" aria-checked={lang === l} className={lang === l ? 'on' : ''}
          onClick={() => setLocalLang(l)}>{l.toUpperCase()}</button>
      ))}
    </div>
  )
}

// ---- Shared vocabulary ---------------------------------------------------------------------------

export const kindLabel = (k: EntryKind) =>
  k === 'Mod' ? 'Mod' : k === 'Maintenance' ? t('Maintenance', 'Wartung') : t('Expense', 'Ausgabe')

/** "1 entry" / "3 entries" — picks singular or plural. */
export const plural = (n: number, one: [string, string], many: [string, string]) =>
  `${n} ${n === 1 ? t(...one) : t(...many)}`
