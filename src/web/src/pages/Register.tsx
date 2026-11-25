import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api, type Me } from '../api'
import { ErrorText, Field, Spinner } from '../components/ui'
import { keys } from '../hooks'
import { storedLang, t } from '../i18n'
import { Brand } from './Login'

export default function Register() {
  const [params] = useSearchParams()
  const code = params.get('code') ?? ''
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const invite = useQuery({
    queryKey: ['invite', code],
    queryFn: () => api.get<{ valid: boolean; note: string | null }>(`/auth/invite/${encodeURIComponent(code)}`),
    enabled: !!code,
  })

  const register = useMutation({
    mutationFn: () => api.post<Me>('/auth/register', { code, displayName: name, email, password, language: storedLang() }),
    onSuccess: me => { qc.setQueryData(keys.me, me); navigate('/', { replace: true }) },
  })

  if (!code || invite.data?.valid === false) {
    return (
      <div className="auth">
        <Brand />
        <h1>{t('Invite needed', 'Einladung nötig')}</h1>
        <p className="muted">{t('This link is invalid, already used or expired. Ask the admin for a new invite.', 'Dieser Link ist ungültig, schon benutzt oder abgelaufen. Frag den Admin nach einer neuen Einladung.')}</p>
      </div>
    )
  }
  if (invite.isLoading) return <div className="center"><Spinner /></div>

  return (
    <div className="auth">
      <Brand />
      <form className="form" onSubmit={e => { e.preventDefault(); register.mutate() }}>
        <div>
          <h1>{t('Create account', 'Konto erstellen')}</h1>
          <p className="muted" style={{ margin: '6px 0 0' }}>{t("You've been invited", 'Du wurdest eingeladen')}{invite.data?.note ? ` (${invite.data.note})` : ''}.</p>
        </div>
        <Field label={t('Name', 'Name')}>
          <input autoComplete="nickname" required maxLength={50} value={name} onChange={e => setName(e.target.value)} />
        </Field>
        <Field label={t('Email', 'E-Mail')}>
          <input type="email" autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} />
        </Field>
        <Field label={t('Password', 'Passwort')} hint={t('At least 10 characters.', 'Mindestens 10 Zeichen.')}>
          <input type="password" autoComplete="new-password" required minLength={10} value={password} onChange={e => setPassword(e.target.value)} />
        </Field>
        <ErrorText error={register.error} />
        <button className="btn btn-primary btn-block" disabled={register.isPending}>{t('Create account', 'Konto erstellen')}</button>
      </form>
    </div>
  )
}
