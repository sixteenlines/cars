import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Copy, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { api, type AdminUser, type Invite, type Usage } from '../api'
import { ErrorText, Field, TopBar } from '../components/ui'
import { fmtDate } from '../format'

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
      await navigator.share({ title: 'Garage invite', url: link }).catch(() => {})
    } else {
      await navigator.clipboard.writeText(link)
      setCopied(true)
    }
  }

  const open = invites.data?.filter(i => !i.usedAt && new Date(i.expiresAt) > new Date()) ?? []

  return (
    <>
      <TopBar back="/" title="Users & invites" />

      <div className="section" style={{ marginTop: 8 }}>
        <h2>Invite a friend</h2>
        <form className="card card-pad form" onSubmit={e => { e.preventDefault(); create.mutate() }}>
          <Field label="Who is it for? (optional)"><input value={note} onChange={e => setNote(e.target.value)} placeholder="e.g. Max" /></Field>
          <button className="btn btn-primary" disabled={create.isPending}>Create invite link</button>
          <ErrorText error={create.error} />
          {link && (
            <div className="stack" style={{ gap: 8 }}>
              <div className="code-box"><span className="grow">{link}</span></div>
              <button type="button" className="btn" onClick={copy}>{copied ? <><Check size={18} /> Copied</> : <><Copy size={18} /> Copy / share link</>}</button>
              <p className="faint" style={{ margin: 0 }}>Valid 14 days, single use. The link is only shown now.</p>
            </div>
          )}
        </form>
      </div>

      {open.length > 0 && (
        <div className="section">
          <h2>Open invites</h2>
          <div className="card list">
            {open.map(i => (
              <div key={i.id} className="list-item">
                <div className="main"><div className="t">{i.note ?? 'Invite'}</div><div className="s">expires {fmtDate(i.expiresAt)}</div></div>
                <button className="icon-btn" aria-label="Revoke invite" onClick={() => revoke.mutate(i.id)}><Trash2 size={18} /></button>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="section">
        <h2>Users</h2>
        <div className="card list">
          {users.data?.map(u => {
            const use = usage.data?.find(x => x.userId === u.id)
            return (
              <div key={u.id} className="list-item">
                <div className="main">
                  <div className="t">{u.displayName}{u.isAdmin && <span className="tag" style={{ marginLeft: 6 }}>admin</span>}</div>
                  <div className="s">{u.email} · {u.vehicleCount} cars · since {fmtDate(u.createdAt)}</div>
                </div>
                {use && (
                  <div className="amount" title="AI bill imports and fuel scans this month (estimated cost) · bill imports total">
                    <div>{use.importsThisMonth} bills{use.fuelScansThisMonth ? ` · ${use.fuelScansThisMonth} fuel` : ''}</div>
                    <div className="faint">{usd.format(use.costThisMonthUsd)} · {use.importsTotal} total</div>
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
