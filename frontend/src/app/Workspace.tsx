import { createContext, useContext, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { request } from '../api/client';
import { setSessionToken, type AuthUser } from './auth';
import { createRealtime } from '../realtime/client';
import type { LiveState } from '../realtime/client';

const WorkspaceContext = createContext<{
  theme: string; setTheme: (value: string) => void; revision: number; refresh: () => void;
  liveState: LiveState; connect: (token: string) => Promise<void>; disconnect: () => Promise<void>;
  live: React.MutableRefObject<ReturnType<typeof createRealtime> | null>;
  sessionVersion: number; hasSession: boolean; getAccessToken: () => string;
  user: AuthUser | null; login: (token: string, user: AuthUser) => Promise<void>; logout: () => Promise<void>;
} | null>(null);
export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [theme, updateTheme] = useState(() => { try { return localStorage.getItem('iti.theme') === 'dark' ? 'dark' : 'light'; } catch { return 'light'; } });
  const [revision, setRevision] = useState(0);
  const [liveState, setLiveState] = useState<LiveState>('offline');
  const live = useRef<ReturnType<typeof createRealtime> | null>(null);
  const accessToken = useRef('');
  const [user, setUser] = useState<AuthUser | null>(null);
  const [sessionVersion, setSessionVersion] = useState(0);
  const refresh = () => setRevision(value => value + 1);
  useEffect(() => { document.documentElement.dataset.theme = theme; try { localStorage.setItem('iti.theme', theme); } catch { /* Theme works without storage. */ } }, [theme]);
  useEffect(() => () => { void live.current?.stop(); }, []);
  useEffect(() => { const clear = () => { accessToken.current=''; setSessionToken(''); setUser(null); setSessionVersion(v=>v+1); void live.current?.stop(); }; window.addEventListener('iti:unauthorized',clear); return ()=>window.removeEventListener('iti:unauthorized',clear); }, []);
  async function disconnect() { const previous = live.current; live.current = null; if (!user) accessToken.current = ''; setSessionVersion(value => value + 1); await previous?.stop(); }
  async function connect(token: string) { await disconnect(); accessToken.current = token; setSessionVersion(value => value + 1); live.current = createRealtime(token, refresh, setLiveState); await live.current.start(); }
  async function logout() { try { await request('/auth/logout', undefined, 15000, { method: 'POST' }); } catch { /* The local token is cleared even when revocation cannot be confirmed. */ } finally { accessToken.current=''; setSessionToken(''); setUser(null); await disconnect(); } }
  async function login(token: string, identity: AuthUser) { setSessionToken(token); setUser(identity); await connect(token); }
  return <WorkspaceContext.Provider value={{ theme, setTheme: updateTheme, revision, refresh, liveState, connect, disconnect, live, sessionVersion, hasSession: Boolean(accessToken.current), getAccessToken: () => accessToken.current, user, login, logout }}>{children}</WorkspaceContext.Provider>;
}
export function useWorkspace() { const context = useContext(WorkspaceContext); if (!context) throw new Error('WorkspaceProvider is required.'); return context; }
