import { useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { ShieldCheck } from 'lucide-react';
import { request } from '../api/client';
import { useWorkspace } from '../app/Workspace';
import { Button, PageHeader } from '../components/ui';
import type { AuthUser } from '../app/auth';
export default function Login() {
  const { user, login } = useWorkspace(); const navigate = useNavigate();
  const [email, setEmail] = useState(''), [password, setPassword] = useState(''), [pending, setPending] = useState(false), [error, setError] = useState('');
  if (user) return <Navigate to="/dashboard" replace />;
  return <main className="login-page"><section className="panel ticket-editor"><ShieldCheck size={28} aria-hidden="true" /><PageHeader eyebrow="IT TICKET INTELLIGENCE" title="Welcome back." description="Sign in to your support workspace." /><form onSubmit={async e => {
    e.preventDefault(); if (pending) return; setPending(true); setError('');
    try { const result = await request<{ accessToken: string; user: AuthUser }>('/auth/login', undefined, 15000, { method: 'POST', body: { email, password } }); setPassword(''); await login(result.accessToken, result.user); navigate('/dashboard', { replace: true }); }
    catch { setPassword(''); setError('Unable to sign in. Check your email and password, or try again later.'); }
    finally { setPending(false); }
  }}><fieldset disabled={pending}><div className="form-field"><label htmlFor="login-email">Email</label><input id="login-email" type="email" autoComplete="username" required maxLength={254} value={email} onChange={e => setEmail(e.target.value)} /></div><div className="form-field"><label htmlFor="login-password">Password</label><input id="login-password" type="password" autoComplete="current-password" required maxLength={256} value={password} onChange={e => setPassword(e.target.value)} /></div></fieldset>{error && <p className="inline-error" role="alert">{error}</p>}<div className="form-actions"><Button className="primary" loading={pending} type="submit">Sign in</Button></div></form><p className="field-help">To protect your account, sign out when you finish.</p></section></main>;
}
