import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import en from './en.json'
import tr from './tr.json'

export type Lang = 'en' | 'tr'
type Catalog = Record<string, string>
/** Merges per-page translation catalogs into one catalog object. */
const merge = (mods: Record<string, Catalog>): Catalog => Object.assign({}, ...Object.values(mods))
const extraEn = merge(import.meta.glob<Catalog>('./extra/*.en.json', { eager: true, import: 'default' }))
const extraTr = merge(import.meta.glob<Catalog>('./extra/*.tr.json', { eager: true, import: 'default' }))
const catalogs: Record<Lang, Catalog> = { en: { ...en, ...extraEn }, tr: { ...tr, ...extraTr } }

type Params = Record<string, string | number>
export type TFn = (key: string, params?: Params) => string

type Ctx = { lang: Lang; locale: string; setLang: (l: Lang) => void; t: TFn }
const I18nContext = createContext<Ctx | null>(null)

/** Reads the stored language choice from browser storage, or null if unavailable. */
const stored = (): Lang | null => {
  try { const v = localStorage.getItem('admin-lang'); return v === 'en' || v === 'tr' ? v : null } catch { return null }
}
/** Picks the language from the browser setting, Turkish if it starts with tr, otherwise English. */
const browser = (): Lang => (navigator.language || '').toLowerCase().startsWith('tr') ? 'tr' : 'en'

/** Replaces named placeholders in a translated text with the given parameter values. */
function format(text: string, params?: Params) {
  if (!params) return text
  return text.replace(/\{(\w+)\}/g, (m, k: string) => (k in params ? String(params[k]) : m))
}

/** React provider that supplies the current language and translation functions to the admin panel. */
export function I18nProvider({ children, initial }: { children: ReactNode; initial?: Lang }) {
  const [lang, setLangState] = useState<Lang>(initial ?? stored() ?? browser())
  const setLang = useCallback((l: Lang) => {
    setLangState(l)
    try { localStorage.setItem('admin-lang', l) } catch {  }
    document.documentElement.lang = l
  }, [])
  const value = useMemo<Ctx>(() => {
    const t: TFn = (key, params) => format(catalogs[lang][key] ?? catalogs.en[key] ?? key, params)
    return { lang, locale: lang === 'tr' ? 'tr-TR' : 'en-US', setLang, t }
  }, [lang, setLang])
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

/** React hook that returns the admin panel's language context. */
export function useI18n() {
  const c = useContext(I18nContext)
  if (!c) throw new Error('I18nProvider yok')
  return c
}
/** React hook that returns the translation function. */
export const useT = () => useI18n().t

/** React hook that returns a translator falling back to the raw text for keys missing from the catalog, such as server codes. */
export function useTOr() {
  const { lang } = useI18n()
  return (key: string, raw: string) => catalogs[lang][key] ?? catalogs.en[key] ?? raw
}
