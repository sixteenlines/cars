import { useMutation, useQueryClient } from '@tanstack/react-query'
import { FileSpreadsheet, LogOut } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { api, type Me } from '../api'
import { ErrorText, Field, TopBar } from '../components/ui'
import { keys, useMe } from '../hooks'
import { browserLang, t, useLang, type Lang } from '../i18n'

export default function Settings() {
  const { data: me } = useMe()
  const { setAccountLang } = useLang()
  const qc = useQueryClient()
  const navigate = useNavigate()

  const saveLang = useMutation({
    mutationFn: (language: Lang | null) => api.put<Me>('/auth/settings', { language }),
    onSuccess: updated => { setAccountLang(updated.language); qc.setQueryData(keys.me, updated) },
  })

  const logout = useMutation({
    mutationFn: () => api.post('/auth/logout'),
    onSuccess: () => { qc.clear(); navigate('/login') },
  })

  if (!me) return null
  const auto = browserLang() === 'de' ? 'Deutsch' : 'English'

  return (
    <>
      <TopBar back="/" title={t('Settings', 'Einstellungen')} />
      <div className="form" style={{ marginTop: 8 }}>
        <div className="card card-pad">
          <dl className="kv" style={{ margin: 0 }}>
            <dt>{t('Name', 'Name')}</dt><dd>{me.displayName}</dd>
            <dt>{t('Email', 'E-Mail')}</dt><dd style={{ overflowWrap: 'anywhere' }}>{me.email}</dd>
          </dl>
        </div>

        <Field label={t('Language', 'Sprache')}
          hint={t('“Automatic” follows your phone or browser. Saved to your account, so it applies on every device.',
            '„Automatisch“ richtet sich nach deinem Handy bzw. Browser. Wird in deinem Konto gespeichert und gilt auf allen Geräten.')}>
          <select value={me.language ?? ''} disabled={saveLang.isPending}
            onChange={e => saveLang.mutate((e.target.value || null) as Lang | null)}>
            <option value="">{t('Automatic', 'Automatisch')} ({auto})</option>
            <option value="de">Deutsch</option>
            <option value="en">English</option>
          </select>
        </Field>
        <ErrorText error={saveLang.error} />

        <Link to="/import/tmm" className="btn btn-block" style={{ marginTop: 16 }}>
          <FileSpreadsheet size={18} /> {t('Import from Track My Mods', 'Import aus Track My Mods')}
        </Link>

        <button type="button" className="btn btn-block" disabled={logout.isPending} onClick={() => logout.mutate()}>
          <LogOut size={18} /> {t('Sign out', 'Abmelden')}
        </button>
        <ErrorText error={logout.error} />
      </div>
    </>
  )
}
