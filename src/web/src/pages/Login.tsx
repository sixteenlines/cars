import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { api, type Me } from '../api'
import { ErrorText, Field } from '../components/ui'
import { keys, useMe } from '../hooks'
import { LangSwitch, t } from '../i18n'

export function Brand() {
  return (
    <div className="brand-row">
      <div className="brand"><img src="/icon-192.png?v=2" alt="" /> Car Tracker</div>
      <LangSwitch />
    </div>
  )
}

export default function Login() {
  const { data: me } = useMe()
  const navigate = useNavigate()
  const location = useLocation()
  const qc = useQueryClient()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  const login = useMutation({
    mutationFn: () => api.post<Me>('/auth/login', { email, password }),
    onSuccess: me => { qc.setQueryData(keys.me, me); navigate(from, { replace: true }) },
  })

  if (me) return <Navigate to={from} replace />

  return (
    <div className="auth">
      <Brand />
      <form className="form" onSubmit={e => { e.preventDefault(); login.mutate() }}>
        <h1>{t('Sign in', 'Anmelden')}</h1>
        <Field label={t('Email', 'E-Mail')}>
          <input type="email" autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} />
        </Field>
        <Field label={t('Password', 'Passwort')}>
          <input type="password" autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} />
        </Field>
        <ErrorText error={login.error} />
        <button className="btn btn-primary btn-block" disabled={login.isPending}>{t('Sign in', 'Anmelden')}</button>
        <p className="faint">{t('No account? Ask the admin for an invite link.', 'Kein Konto? Frag den Admin nach einem Einladungslink.')}</p>
      </form>
    </div>
  )
}
