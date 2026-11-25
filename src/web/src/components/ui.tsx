import { ChevronLeft, LoaderCircle } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { t } from '../i18n'

export const Spinner = ({ size = 22 }: { size?: number }) => <LoaderCircle size={size} className="spin" aria-label={t('Loading', 'Lädt')} />

export function TopBar({ title, back, children }: { title?: React.ReactNode; back?: string | true; children?: React.ReactNode }) {
  const navigate = useNavigate()
  return (
    <header className="topbar">
      {back && (
        <button className="icon-btn" aria-label={t('Back', 'Zurück')} style={{ marginLeft: -10 }}
          onClick={() => (back === true ? navigate(-1) : navigate(back))}>
          <ChevronLeft size={26} />
        </button>
      )}
      <div className="title">{title}</div>
      {children}
    </header>
  )
}

export function Empty({ icon, title, children }: { icon?: React.ReactNode; title: string; children?: React.ReactNode }) {
  return (
    <div className="empty">
      {icon}
      <h3>{title}</h3>
      {children && <div style={{ marginTop: 6 }}>{children}</div>}
    </div>
  )
}

export function Field({ label, children, hint }: { label: string; children: React.ReactNode; hint?: string }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {hint && <small className="faint">{hint}</small>}
    </label>
  )
}

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null
  return <p className="error" role="alert">{error instanceof Error ? error.message : String(error)}</p>
}

/**
 * Two-tap delete button: first tap arms it ("Really …?"), second tap confirms.
 * Avoids window.confirm(), which some browsers / PWA modes silently suppress.
 */
export function ConfirmButton({ children, confirmLabel, onConfirm, className = 'btn btn-danger btn-ghost', disabled, ariaLabel }: {
  children: React.ReactNode; confirmLabel: React.ReactNode; onConfirm: () => void
  className?: string; disabled?: boolean; ariaLabel?: string
}) {
  const [armed, setArmed] = useState(false)
  useEffect(() => {
    if (!armed) return
    const t = setTimeout(() => setArmed(false), 4000)
    return () => clearTimeout(t)
  }, [armed])
  return (
    <button type="button" className={`${className}${armed ? ' armed' : ''}`} disabled={disabled} aria-label={ariaLabel}
      onClick={() => { if (armed) { setArmed(false); onConfirm() } else setArmed(true) }}>
      {armed ? confirmLabel : children}
    </button>
  )
}
