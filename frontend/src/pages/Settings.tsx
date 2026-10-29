import { Link } from 'react-router-dom';
import { Monitor, Radio, ShieldCheck } from 'lucide-react';
import { useWorkspace } from '../app/Workspace';
import { Button, PageHeader, SectionHeader } from '../components/ui';
export default function Settings() {
  const { theme, setTheme, liveState, connect, disconnect, user, logout, getAccessToken } = useWorkspace();
  return <><PageHeader eyebrow="WORKSPACE SETTINGS" title="Make room for your work." description="Presentation preferences and your signed-in session." />
    <section className="panel settings-panel"><SectionHeader title="Appearance" description="Choose a theme for this browser." /><div className="setting-row"><div><Monitor size={21} aria-hidden="true" /><span>Workspace theme</span></div><div className="segmented" aria-label="Workspace theme"><button aria-pressed={theme==='light'} onClick={()=>setTheme('light')}>Light</button><button aria-pressed={theme==='dark'} onClick={()=>setTheme('dark')}>Dark</button></div></div></section>
    <section className="panel settings-panel"><SectionHeader title="Your session" description={`${user?.name ?? ''} · ${user?.role ?? ''}`} /><div className="connection-status" role="status"><Radio size={21} aria-hidden="true" /><span>Live updates: {liveState}</span></div><div className="form-actions"><Button disabled={liveState==='connected'||liveState==='connecting'} onClick={()=>void connect(getAccessToken())}>Connect live updates</Button><Button disabled={liveState==='offline'} onClick={()=>void disconnect()}>Disconnect live updates</Button><Button onClick={()=>void logout()}>Sign out</Button></div><p className="field-help"><ShieldCheck size={17} aria-hidden="true" />Your access token stays in memory. Reloading this page requires signing in again.</p></section>
    {user?.role==='Admin' && <section className="panel settings-panel"><SectionHeader title="User accounts" description="Create accounts and assign roles for your team."/><Link className="button primary" to="/admin/users">Manage users</Link></section>}
  </>;
}
