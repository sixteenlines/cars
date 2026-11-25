import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { api, type Me } from '../api'
import { ErrorText, Field } from '../components/ui'
import { keys, useMe } from '../hooks'

export function Brand() {
  return <div className="brand"><img src="/icon-192.png" alt="" /> Car Tracker</div>
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
        <h1>Sign in</h1>
        <Field label="Email">
          <input type="email" autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} />
        </Field>
        <Field label="Password">
          <input type="password" autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} />
        </Field>
        <ErrorText error={login.error} />
        <button className="btn btn-primary btn-block" disabled={login.isPending}>Sign in</button>
        <p className="faint">No account? Ask the admin for an invite link.</p>
      </form>
    </div>
  )
}
