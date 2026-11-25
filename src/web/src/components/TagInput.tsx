import { X } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTags } from '../hooks'
import { t as tr } from '../i18n'

export const normalizeTag = (raw: string) =>
  raw.trim().replace(/^#+/, '').trim().toLowerCase().split(/\s+/).filter(Boolean).join('_').slice(0, 50)

/** Free tags: type + Enter/comma to add, tap suggestions (most used first). */
export function TagInput({ value, onChange }: { value: string[]; onChange: (tags: string[]) => void }) {
  const [text, setText] = useState('')
  const { data: all = [] } = useTags()

  const add = (raw: string) => {
    const t = normalizeTag(raw)
    if (t && !value.includes(t)) onChange([...value, t])
    setText('')
  }

  const suggestions = useMemo(() => {
    const q = normalizeTag(text)
    return all.map(t => t.name).filter(n => !value.includes(n) && (!q || n.includes(q))).slice(0, q ? 8 : 10)
  }, [all, text, value])

  return (
    <div>
      <div className="tag-input">
        {value.map(t => (
          <span key={t} className="tag">#{t}
            <button type="button" aria-label={tr(`Remove ${t}`, `${t} entfernen`)} onClick={() => onChange(value.filter(x => x !== t))}><X size={14} /></button>
          </span>
        ))}
        <input
          value={text}
          placeholder={value.length ? '' : tr('e.g. suspension, track, oem', 'z. B. fahrwerk, rennstrecke, oem')}
          enterKeyHint="done"
          onChange={e => {
            const v = e.target.value
            if (/[,;]$/.test(v)) add(v.slice(0, -1))
            else setText(v)
          }}
          onKeyDown={e => {
            if (e.key === 'Enter') { e.preventDefault(); if (text.trim()) add(text) }
            if (e.key === 'Backspace' && !text && value.length) onChange(value.slice(0, -1))
          }}
          onBlur={() => text.trim() && add(text)}
        />
      </div>
      {suggestions.length > 0 && (
        <div className="suggestions">
          {suggestions.map(s => (
            <button type="button" key={s} className="chip" onMouseDown={e => e.preventDefault()} onClick={() => add(s)}>#{s}</button>
          ))}
        </div>
      )}
    </div>
  )
}
