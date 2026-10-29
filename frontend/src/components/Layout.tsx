import { Activity, ArrowUpRight, BookOpen, Bot, ChartNoAxesCombined, ChevronRight, Command, LayoutDashboard, LogOut, Menu, Moon, Settings2, Sun, Ticket, Users, X } from 'lucide-react';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { useWorkspace } from '../app/Workspace';

const navigation = [
  { to: '/admin/users', label: 'Users', icon: Users },
  { to: '/dashboard', label: 'Overview', icon: LayoutDashboard },
  { to: '/tickets', label: 'Tickets', icon: Ticket },
  { to: '/ai-assistant', label: 'AI assistant', icon: Bot },
  { to: '/knowledge', label: 'Knowledge base', icon: BookOpen },
  { to: '/analytics', label: 'Analytics', icon: ChartNoAxesCombined },
];
const connectionLabels = { offline: 'Live updates off', connecting: 'Connecting', connected: 'Live updates on', reconnecting: 'Reconnecting', error: 'Connection interrupted' };
export function Layout() {
  const { pathname } = useLocation();
  const [menu, setMenu] = useState(false);
  const { theme, setTheme, liveState, user, logout } = useWorkspace();
  const current = navigation.find(item => pathname.startsWith(item.to))?.label ?? (pathname === '/settings' ? 'Settings' : 'Workspace');
  useEffect(() => { setMenu(false); document.title = `${current} | IT Ticket Intelligence`; requestAnimationFrame(() => document.querySelector<HTMLElement>('h1')?.focus()); }, [pathname, current]);
  useEffect(() => {
    if (!menu) return;
    const toggle = document.querySelector<HTMLButtonElement>('.mobile-menu');
    const items = [toggle, ...document.querySelectorAll<HTMLAnchorElement>('.sidebar a')].filter((node): node is HTMLButtonElement | HTMLAnchorElement => Boolean(node));
    items[1]?.focus();
    const keyboard = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { setMenu(false); toggle?.focus(); }
      if (event.key === 'Tab') { event.preventDefault(); const current = items.indexOf(document.activeElement as HTMLAnchorElement); items[(current + (event.shiftKey ? -1 : 1) + items.length) % items.length]?.focus(); }
    };
    const resize = () => { if (window.matchMedia('(min-width: 64rem)').matches) setMenu(false); };
    window.addEventListener('keydown', keyboard); window.addEventListener('resize', resize);
    return () => { window.removeEventListener('keydown', keyboard); window.removeEventListener('resize', resize); };
  }, [menu]);
  return <div className="app-shell"><a className="skip-link" href="#main">Skip to content</a>
    <aside id="workspace-navigation" className={`sidebar ${menu ? 'is-open' : ''}`} aria-label="Workspace navigation">
      <Link to="/dashboard" className="brand"><span className="brand-mark"><Command size={22} aria-hidden="true" /></span><span>ticket<span className="brand-light">intelligence</span></span></Link>
      <div className="workspace-label"><span className="workspace-avatar">IT</span><div>IT Operations<small>Local workspace</small></div></div>
      <div className="nav-caption">WORKSPACE</div>
      <nav aria-label="Main navigation">{navigation.filter(item => (item.to !== '/admin/users' || user?.role === 'Admin') && (item.to !== '/dashboard' || user?.role === 'Admin') && (item.to !== '/knowledge' || user?.role !== 'User') && (item.to !== '/ai-assistant' || user?.role !== 'User')).map(({ to, label, icon: Icon }) => <NavLink key={to} to={to} className={({ isActive }) => `nav-link ${isActive ? 'selected' : ''}`}><Icon size={19} aria-hidden="true" /><span>{label}</span>{to === '/ai-assistant' && <span className="nav-tag">AI</span>}</NavLink>)}</nav>
      <div className="sidebar-bottom"><div className="workspace-note"><Activity size={19} aria-hidden="true" /><strong>Intelligence, with oversight.</strong><p>Predictions inform decisions. Your team stays in control.</p><Link to="/ai-assistant">Explore the workflow<ArrowUpRight size={14} aria-hidden="true" /></Link></div><NavLink className="nav-link" to="/settings"><Settings2 size={19} aria-hidden="true" />Settings</NavLink><div className="sidebar-footer"><span className="user-avatar">IT</span><div>{user?.name}<small>{user?.role}</small></div></div></div>
    </aside>
    <div className="workspace-main"><header className="topbar"><button className="icon-button mobile-menu" aria-label={menu ? 'Close navigation' : 'Open navigation'} aria-expanded={menu} aria-controls="workspace-navigation" onClick={() => setMenu(!menu)}>{menu ? <X size={20} /> : <Menu size={20} />}</button><div className="breadcrumbs"><span>Workspace</span><ChevronRight size={14} aria-hidden="true" /><strong>{current}</strong></div><div className="topbar-actions"><button className="icon-button" aria-label="Sign out" title="Sign out" onClick={() => void logout()}><LogOut size={18} aria-hidden="true" /></button><Link to="/settings" aria-label={connectionLabels[liveState]} className={`connection-label live-${liveState}`}><span className="live-dot" aria-hidden="true" /><span>{connectionLabels[liveState]}</span></Link><button className="icon-button" aria-label={`Use ${theme === 'light' ? 'dark' : 'light'} theme`} onClick={() => setTheme(theme === 'light' ? 'dark' : 'light')}>{theme === 'light' ? <Moon size={18} /> : <Sun size={18} />}</button></div></header>
      <main id="main" inert={menu || undefined} className="main-content"><Outlet /></main><footer inert={menu || undefined} className="page-footer"><span>IT Ticket Intelligence</span><span>Human decisions. Assisted by intelligence.</span></footer>
    </div></div>;
}
