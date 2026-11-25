import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Copy, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { api, type AdminUser, type Invite, type Usage } from '../api'
import { ErrorText, Field, TopBar } from '../components/ui'
import { fmtDate } from '../format'
import { plural, t } from '../i18n'

export default function Admin() {
  const qc = useQueryClient()
  const invites = useQuery({ queryKey: ['admin', 'invites'], queryFn: () => api.get<Invite[]>('/admin/invites') })
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: () => api.get<AdminUser[]>('/admin/users') })
  const usage = useQuery({ queryKey: ['admin', 'usage'], queryFn: () => api.get<Usage[]>('/admin/usage') })
  const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 3 })
  const [note, setNote] = useState('')
  const [link, setLink] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const create = useMutation({
    mutationFn: () => api.post<{ code: string }>('/admin/invites', { note, validDays: 14, grantsAdmin: false }),
    onSuccess: res => {
      setLink(`${location.origin}/register?code=${encodeURIComponent(res.code)}`)
      setCopied(false)
      setNote('')
      qc.invalidateQueries({ queryKey: ['admin', 'invites'] })
    },
  })
  const revoke = useMutation({
    mutationFn: (id: number) => api.del(`/admin/invites/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'invites'] }),
  })

  const copy = async () => {
    if (!link) return
    if (navigator.share && /iPhone|iPad|Android/i.test(navigator.userAgent)) {
      await navigator.share({ title: t('Car Tracker invite', 'Einladung zu Car Tracker'), url: link }).catch(() => {})
    } else {
      await navigator.clipboard.writeText(link)
      setCopied(true)
    }
  }

  const open = invites.data?.filter(i => !i.usedAt && new Date(i.expiresAt) > new Date()) ?? []

  return (
    <>
      <TopBar back="/" title={t('Users & invites', 'Nutzer & Einladungen')} />

      <div className="section" style={{ marginTop: 8 }}>
        <h2>{t('Invite a friend', 'Freund einladen')}</h2>
        <form className="card card-pad form" onSubmit={e => { e.preventDefault(); create.mutate() }}>
          <Field label={t('Who is it for? (optional)', 'Für wen? (optional)')}><input value={note} onChange={e => setNote(e.target.value)} placeholder={t('e.g. Max', 'z. B. Max')} /></Field>
          <button className="btn btn-primary" disabled={create.isPending}>{t('Create invite link', 'Einladungslink erstellen')}</button>
          <ErrorText error={create.error} />
          {link && (
            <div className="stack" style={{ gap: 8 }}>
              <div className="code-box"><span className="grow">{link}</span></div>
              <button type="button" className="btn" onClick={copy}>{copied ? <><Check size={18} /> {t('Copied', 'Kopiert')}</> : <><Copy size={18} /> {t('Copy / share link', 'Link kopieren / teilen')}</>}</button>
              <p className="faint" style={{ margin: 0 }}>{t('Valid 14 days, single use. The link is only shown now.', '14 Tage gültig, nur einmal nutzbar. Der Link wird nur jetzt angezeigt.')}</p>
            </div>
          )}
        </form>
      </div>

      {open.length > 0 && (
        <div className="section">
          <h2>{t('Open invites', 'Offene Einladungen')}</h2>
          <div className="card list">
            {open.map(i => (
              <div key={i.id} className="list-item">
                <div className="main"><div className="t">{i.note ?? t('Invite', 'Einladung')}</div><div className="s">{t('expires', 'läuft ab am')} {fmtDate(i.expiresAt)}</div></div>
                <button className="icon-btn" aria-label={t('Revoke invite', 'Einladung zurückziehen')} onClick={() => revoke.mutate(i.id)}><Trash2 size={18} /></button>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="section">
        <h2>{t('Users', 'Nutzer')}</h2>
        <div className="card list">
          {users.data?.map(u => {
            const use = usage.data?.find(x => x.userId === u.id)
            return (
              <div key={u.id} className="list-item">
                <div className="main">
                  <div className="t">{u.displayName}{u.isAdmin && <span className="tag" style={{ marginLeft: 6 }}>admin</span>}</div>
                  <div className="s">{u.email} · {plural(u.vehicleCount, ['car', 'Auto'], ['cars', 'Autos'])} · {t('since', 'seit')} {fmtDate(u.createdAt)}</div>
                </div>
                {use && (
                  <div className="amount" title={t('AI bill imports and fuel scans this month (estimated cost) · bill imports total', 'KI-Rechnungsimporte und Tank-Scans diesen Monat (geschätzte Kosten) · Rechnungsimporte gesamt')}>
                    <div>{use.importsThisMonth} {t('bills', 'Rechnungen')}{use.fuelScansThisMonth ? ` · ${use.fuelScansThisMonth} ${t('fuel', 'Tank-Scans')}` : ''}</div>
                    <div className="faint">{usd.format(use.costThisMonthUsd)} · {use.importsTotal} {t('total', 'gesamt')}</div>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </div>
    </>
  )
}
