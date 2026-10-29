import { createBrowserRouter, createRoutesFromElements, Link, Navigate, Route, RouterProvider, Outlet } from 'react-router-dom';
import { WorkspaceProvider } from './app/Workspace';
import { Layout } from './components/Layout';
import { PageHeader } from './components/ui';
import Dashboard from './pages/Dashboard';
import Tickets from './pages/Tickets';
import TicketDetail from './pages/TicketDetail';
import Assistant from './pages/Assistant';
import Knowledge, { KnowledgeDetail } from './pages/Knowledge';
import Analytics from './pages/Analytics';
import Settings from './pages/Settings';
import Login from './pages/Login';
import './styles/users.css';
import { useWorkspace } from './app/Workspace';
function RoleRoute({roles,children}:{roles:string[];children:React.ReactNode}) { const {user}=useWorkspace(); return user && roles.includes(user.role) ? children : <Navigate to="/tickets" replace />; }
function DashboardAccess() { const {user}=useWorkspace(); return user?.role === 'Admin' ? <Dashboard /> : <Navigate to="/tickets" replace />; }
function AuthOutlet() { return <Outlet />; }
function SessionLayout() { const { user } = useWorkspace(); return user ? <Layout /> : <Navigate to="/login" replace />; }
import { lazy, Suspense } from 'react';
const CreateKnowledge=lazy(()=>import('./pages/CreateKnowledge'));
const Users=lazy(()=>import('./pages/Users'));
const CreateTicketPage = lazy(() => import('./pages/CreateTicketPage'));
const formFallback = <p className="section-empty" role="status">Loading ticket form…</p>;
import '@fontsource-variable/inter';
import './styles/theme.css';
import './styles/app.css';
import './styles/ticket-form.css';

const router = createBrowserRouter(createRoutesFromElements(<Route element={<WorkspaceProvider><AuthOutlet /></WorkspaceProvider>}>
    <Route path="login" element={<Login />} />
    <Route element={<SessionLayout />}>
    <Route index element={<Navigate to="/dashboard" replace />} />
    <Route path="dashboard" element={<DashboardAccess />} />
    <Route path="tickets" element={<Tickets />} />
    <Route path="tickets/new" element={<Suspense fallback={formFallback}><RoleRoute roles={['Admin','Support','User']}><CreateTicketPage /></RoleRoute></Suspense>} />
    <Route path="tickets/:id/edit" element={<Suspense fallback={formFallback}><RoleRoute roles={['Admin','Support']}><CreateTicketPage edit /></RoleRoute></Suspense>} />
    <Route path="tickets/:id" element={<TicketDetail />} />
    <Route path="ai-assistant" element={<RoleRoute roles={['Admin','Support','Agent']}><Assistant /></RoleRoute>} />
    <Route path="knowledge" element={<RoleRoute roles={['Admin','Agent','Support']}><Knowledge /></RoleRoute>} />
    <Route path="knowledge/new" element={<RoleRoute roles={['Admin']}><Suspense fallback={<p role="status">Loading document form…</p>}><CreateKnowledge /></Suspense></RoleRoute>} />
    <Route path="knowledge/:id" element={<RoleRoute roles={['Admin','Agent','Support']}><KnowledgeDetail /></RoleRoute>} />
    <Route path="analytics" element={<Analytics />} />
    <Route path="settings" element={<Settings />} />
    <Route path="admin/users" element={<Suspense fallback={<p role="status">Loading account administration�</p>}><RoleRoute roles={['Admin']}><Users /></RoleRoute></Suspense>} />
    <Route path="*" element={<><PageHeader eyebrow="PAGE NOT FOUND" title="This path leads nowhere." description="Return to the overview to continue working." /><Link className="button primary" to="/dashboard">Return to overview</Link></>} />
  </Route></Route>));
export default function App() { return <RouterProvider router={router} />; }
