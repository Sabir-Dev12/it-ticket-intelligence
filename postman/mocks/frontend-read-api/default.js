// Synthetic read-only contract fixture for visual and HTTP integration tests only.
// Never imported by the frontend or used as an automatic production fallback.
const http = require('http');
const titles = [
  ['Production SQL connection pool exhausted', 'Database', 'Critical'],
  ['VPN disconnects after workstation sleep', 'Network', 'High'],
  ['Invoice PDF fails for enterprise accounts', 'Billing', 'Medium'],
  ['New starter cannot complete account activation', 'Account', 'Medium'],
  ['Deployment health check returns 503', 'Application', 'High'],
  ['External monitor not detected over USB-C', 'Hardware', 'Low'],
  ['Suspicious login from an unfamiliar device', 'Security', 'High'],
  ['Desktop client crashes when exporting reports', 'Software', 'Medium'],
];
const tickets = Array.from({ length: 36 }, (_, i) => {
  const [title, category, priority] = titles[i % titles.length];
  const instant = new Date(Date.UTC(2026, 9, 27, 16, 40 - i)).toISOString();
  return { id: `10000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`, reference: `TKT-${String(1045 + i)}`,
    title, description: `${title}. The issue was reported by the service desk. Please investigate the affected service and document the findings.`,
    createdById: '20000000-0000-4000-8000-000000000001', status: ['Open', 'InProgress', 'Open', 'Resolved'][i % 4],
    category, priority, assignedTeamId: null, revision: 1, createdAt: instant, updatedAt: instant, isArchived: false,
    archivedAt: null, commentCount: 1, predictionCount: 0 };
});
const documents = [
  ['SQL Server connection timeout', 'Database', 'TroubleshootingGuide'], ['VPN reconnection procedure', 'Network', 'Procedure'],
  ['Invoice export troubleshooting', 'Billing', 'Article'], ['Account activation FAQ', 'Account', 'Faq'],
  ['Deployment rollback checklist', 'Application', 'Procedure'], ['Endpoint security triage', 'Security', 'Document'],
].map(([title, category, kind], i) => ({ id: `30000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`,
  title, category, kind, source: `knowledge-base/${category.toLowerCase()}/guide.md`, format: 'Markdown', version: 1,
  updatedAt: '2026-10-26T14:00:00Z', chunkCount: 3 }));
const seedTickets = structuredClone(tickets);
const details = new Map();
const analyses = new Map();
const teams = ['Database', 'Network', 'Support', 'Backend', 'Security', 'Infrastructure'].map((name, i) => ({ id: `50000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`, name, isActive: true }));
let failureMode = '';
const conversations = new Map();
const accountSeed={id:'20000000-0000-4000-8000-000000000001',name:'Fixture administrator',email:'admin@example.test',role:'Admin',isActive:true};
const accounts=new Map([[accountSeed.email,accountSeed]]);
const attachments = new Map();
const assistantTokens = new Set(['synthetic-jwt-ui-fixture-not-a-production-token', 'assistant-browser-session-token-0123456789abcdef', 'assistant-other-session-token-0123456789abcdef']);
async function assistantRoute(req, url, send, body) {
  const token = String(req.headers.authorization || '').replace(/^Bearer /, '');
  if (!assistantTokens.has(token)) return send(401, { title: 'Authenticated session required' });
  const path = url.pathname.split('/').filter(Boolean);
  if (path.length === 3 && req.method === 'GET') {
    const page = Number(url.searchParams.get('page')) || 1;
    const rows = [...conversations.values()].filter(c => c.owner === token).sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
    return send(200, { items: rows.slice((page - 1) * 20, page * 20).map(c => ({ id: c.id, title: c.title, ticketId: c.ticketId, ticketReference: c.ticketReference, updatedAt: c.updatedAt })), page, hasMore: rows.length > page * 20 });
  }
  const publicConversation = c => { const { owner, ...result } = c; return result; };
  if (path.length === 3 && req.method === 'POST') {
    const input = await body();
    const references = [...input.question.matchAll(/(?:IT-|ticket\s*#?\s*|#)(\d+)/ig)].map(m => 'TKT-' + Number(m[1]));
    const ticket = tickets.find(t => input.ticketId ? t.id === input.ticketId : references.includes(t.reference));
    if (!references.length && !input.ticketId) return send(400, { title: 'Invalid question', errors: { ticket: ['Mention one ticket, for example: Why is ticket #1045 failing?'] } });
    if (!ticket) return send(404, { title: 'Ticket not found' });
    const existing = conversations.get(input.requestId);
    if (existing) return existing.owner === token ? send(201, publicConversation(existing)) : send(404, { title: 'Conversation not found' });
    const now = new Date().toISOString();
    const conversation = { id: input.requestId, owner: token, ticketId: ticket.id, ticketReference: ticket.reference.replace('TKT-', 'IT-'), ticketRevision: ticket.revision, title: input.question.slice(0, 160), createdAt: now, updatedAt: now, turns: [] };
    conversations.set(conversation.id, conversation); return send(201, publicConversation(conversation));
  }
  const conversation = conversations.get(path[3]);
  if (!conversation || conversation.owner !== token) return send(404, { title: 'Conversation not found' });
  if (path.length === 4 && req.method === 'GET') return send(200, publicConversation(conversation));
  if (path.length === 5 && path[4] === 'messages' && req.method === 'POST') {
    const input = await body(); const ticket = tickets.find(t => t.id === conversation.ticketId);
    const existing = conversation.turns.find(t => t.id === input.requestId);
    if (existing) return send(200, publicConversation(conversation));
    if (conversation.turns.some(t => t.state === 'Pending')) return send(409, { title: 'A response is already pending' });
    if (failureMode === 'assistant-transport-error') return send(503, { title: 'Synthetic transport unavailable' });
    const createdAt = new Date().toISOString(), runId = require('crypto').randomUUID();
    const turn = { id: input.requestId, runId, state: 'Pending', userMessage: { id: require('crypto').randomUUID(), role: 'User', content: input.content, createdAt }, assistantMessage: null,
      analysis: null, errorCode: null, createdAt, updatedAt: createdAt,
      activity: [{ callId: 'host-ticket', name: 'get_ticket', state: 'Succeeded', occurredAt: createdAt }, { callId: 'host-history', name: 'get_ticket_history', state: 'Succeeded', occurredAt: createdAt }, { callId: 'host-knowledge', name: 'search_knowledge', state: 'Started', occurredAt: createdAt }] };
    conversation.turns.push(turn); conversation.updatedAt = createdAt;
    await new Promise(resolve => setTimeout(resolve, 2600));
    const now = new Date().toISOString(); turn.updatedAt = now; conversation.updatedAt = now;
    turn.activity[2].state = 'Succeeded';
    if (failureMode === 'assistant-failure') {
      turn.state = 'Failed'; turn.errorCode = 'DependencyUnavailable'; turn.assistantMessage = { id: require('crypto').randomUUID(), role: 'Assistant', content: 'No completed response was saved.', createdAt: now };
    } else {
      turn.state = 'Completed';
      const analysis = { runId, ticketRevision: ticket.revision, facts: { id: ticket.id, revision: ticket.revision, title: ticket.title, description: ticket.description, status: ticket.status, isArchived: false, assignedTeamId: null },
        predictions: predict(ticket), summary: input.content.includes('first') ? 'First, verify the configured SQL host and listener port.' : 'The reported SQL failure requires a connection-path investigation.',
        diagnosis: 'Hypothesis: the SQL listener may be unreachable.', possibleCauses: ['Hypothesis: a firewall change blocks TCP 1433.'],
        recommendedActions: [{ description: 'Ask an operator to verify the SQL host and TCP port 1433.', requiresHumanApproval: true }],
        sources: [{ documentId: documents[0].id, chunkId: '60000000-0000-4000-8000-000000000001', documentVersion: 1, title: documents[0].title, source: documents[0].source, contentHash: 'a'.repeat(64) }], confidence: .88, requiresHumanReview: true };
      turn.analysis = analysis; turn.assistantMessage = { id: require('crypto').randomUUID(), role: 'Assistant', content: analysis.summary, createdAt: now };
    }
    return send(200, publicConversation(conversation));
  }
  return send(501, { title: 'Assistant route outside the fixture contract' });
}
function detail(ticket) {
  if (!details.has(ticket.id)) details.set(ticket.id, { resolution: null, resolvedAt: null, closedAt: null, predictions: [],
    comments: [{ id: '40000000-0000-4000-8000-000000000001', ticketId: ticket.id, authorId: ticket.createdById, message: 'Initial investigation is in progress. No changes have been applied.', createdAt: ticket.createdAt }] });
  return { ...ticket, ...details.get(ticket.id) };
}
function predict(ticket) {
  const batch = require('crypto').randomUUID(), createdAt = new Date().toISOString();
  const common = { ticketId: ticket.id, inputRevision: ticket.revision, analysisId: batch, createdAt, modelVersion: 'contract-fixture-v1', needsHumanReview: false, reviewThreshold: 0.8, predictedCategory: null, predictedPriority: null, recommendedTeamId: null, recommendedTeamName: null };
  detail(ticket);
  details.get(ticket.id).predictions.push(...[
    { ...common, id: require('crypto').randomUUID(), type: 'Category', confidence: .94, predictedCategory: 'Database' },
    { ...common, id: require('crypto').randomUUID(), type: 'Priority', confidence: .91, predictedPriority: 'High' },
    { ...common, id: require('crypto').randomUUID(), type: 'TeamRouting', confidence: .88, recommendedTeamId: teams[0].id, recommendedTeamName: teams[0].name },
  ]);
  ticket.predictionCount += 3;
  return { category: 'Database', categoryConfidence: .94, priority: 'High', priorityConfidence: .91, recommendedTeam: 'Database', teamConfidence: .88, needsHumanReview: false };
}
http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://local');
  const send = (status, body) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(body)); };
  async function body() {
    if (req.body) return typeof req.body === 'string' ? JSON.parse(req.body) : req.body;
    let raw = ''; for await (const part of req) raw += part;
    req.body=raw ? JSON.parse(raw) : {};return req.body;
  }
  if (req.method === 'POST' && url.pathname === '/__test/reset') {
    accounts.clear();accounts.set(accountSeed.email,accountSeed);
    tickets.splice(0, tickets.length, ...structuredClone(seedTickets)); details.clear(); analyses.clear(); conversations.clear(); attachments.clear(); failureMode = '';
    return send(200, { reset: true });
  }
  if (req.method === 'POST' && url.pathname === '/__test/mode') {
    failureMode = (await body()).mode;
    if (failureMode === 'closed') tickets[0].status = 'Closed';
    return send(200, { configured: true });
  }
  const articleQuestion=url.pathname.match(/^\/api\/assistant\/knowledge\/([^/]+)\/answers$/);
  if(articleQuestion && req.method==='POST'){
    if(!assistantTokens.has(String(req.headers.authorization||'').replace(/^Bearer /,'')))return send(401,{title:'Authenticated session required'});
    const input=await body();const document=documents.find(d=>d.id===articleQuestion[1]);
    if(!document)return send(404,{title:'Article context unavailable'});
    if(!input.question || input.question.length>2000)return send(400,{title:'Invalid question'});
    if(failureMode==='llm-unavailable')return send(503,{title:'Synthetic provider unavailable'});
    return send(200,{answer:{summary:'Article evidence: check SQL host and TCP port 1433.',suggestedSteps:['Review the SQL listener configuration.'],insufficientContext:false},citations:[{documentId:document.id,chunkId:'60000000-0000-4000-8000-000000000001',documentVersion:document.version,title:document.title,source:document.source,contentHash:'a'.repeat(64)}],provider:'Ollama',model:'contract-fixture-v1',attempts:1,needsHumanReview:true});
  }
  if (url.pathname.startsWith('/api/assistant/conversations')) return assistantRoute(req, url, send, body);
  if(url.pathname==='/api/admin/users'){
    if(req.headers.authorization!=='Bearer synthetic-jwt-ui-fixture-not-a-production-token')return send(403,{title:'Administrator access required'});
    if(req.method==='GET')return send(200,[...accounts.values()]);
    if(req.method==='POST'){const input=await body();const email=String(input.email||'').trim().toLowerCase();if(accounts.has(email))return send(409,{title:'Email already exists'});if(!input.name?.trim() || !email.includes('@') || !input.password || input.password.length<14 || !['User','Agent','Support','Admin'].includes(input.role))return send(400,{title:'Invalid account'});const account={id:require('crypto').randomUUID(),name:input.name.trim(),email,role:input.role,isActive:true};accounts.set(email,account);return send(200,account);}
  }
  if(url.pathname==='/api/auth/login' && req.method==='POST'){
    const input=await body();const role={ 'user@example.test':'User','agent@example.test':'Agent','support@example.test':'Support' }[input.email];
    if(role && input.password==='synthetic-password-for-ui-only')return send(200,{accessToken:'synthetic-non-admin-'+role,tokenType:'Bearer',expiresIn:null,user:{id:'20000000-0000-4000-8000-000000000002',name:'Fixture '+role,email:input.email,role}});
  }
  if (url.pathname === '/api/auth/login' && req.method === 'POST') {
    const input = await body();
    if (input.email !== 'admin@example.test' || input.password !== 'synthetic-password-for-ui-only') return send(401, { title: 'Invalid credentials' });
    return send(200, { accessToken: 'synthetic-jwt-ui-fixture-not-a-production-token', tokenType: 'Bearer', expiresIn: null, user: { id: '20000000-0000-4000-8000-000000000001', name: 'Fixture administrator', role: 'Admin' } });
  }
  if (url.pathname === '/api/auth/logout' && req.method === 'POST') return send(200, { signedOut: true });
  if (url.pathname === '/api/tickets/form-options' && req.method === 'GET') return send(200, { teams, teamsTruncated: false });
  const attachmentMatch = url.pathname.match(/^\/api\/tickets\/([^/]+)\/attachments(?:\/([^/]+))?$/);
  if (attachmentMatch) {
    const ticket = tickets.find(t => t.id === attachmentMatch[1]); if (!ticket) return send(404, {});
    const rows = attachments.get(ticket.id) || [];
    if (req.method === 'GET' && !attachmentMatch[2]) return send(200, { items: rows.map(({ data, ...meta }) => meta), revision: ticket.revision });
    if (req.method === 'GET') { const file = rows.find(f => f.id === attachmentMatch[2]); if (!file) return send(404, {}); res.writeHead(200, { 'Content-Type': file.contentType, 'Content-Disposition': 'attachment', 'X-Content-Type-Options': 'nosniff' }); return res.end(file.data); }
    if (req.method === 'DELETE') { if (+url.searchParams.get('revision') !== ticket.revision) return send(409, {}); attachments.set(ticket.id, rows.filter(f => f.id !== attachmentMatch[2])); return send(200, { revision: ++ticket.revision }); }
    if (req.method === 'POST') {
      if (failureMode === 'upload-unavailable') return send(503, { title: 'Synthetic unavailable upload' });
      const raw = []; if (req.body != null) raw.push(Buffer.isBuffer(req.body) ? req.body : Buffer.from(String(req.body))); else for await (const part of req) raw.push(part);
      const buffer = Buffer.concat(raw), boundary = String(req.headers['content-type']).match(/boundary="?([^";]+)"?/)?.[1];
      const parts = buffer.toString('latin1').split('--' + boundary);
      const field = name => parts.find(p => p.includes(`name="${name}"`))?.split('\r\n\r\n')[1]?.replace(/\r\n$/, '');
      const id = field('attachmentId'), revision = Number(field('revision'));
      const filePart = parts.find(p => p.includes('name="file";'));
      if (!filePart) return send(400, { title: 'Missing multipart file' });
      const content = Buffer.from(filePart.split('\r\n\r\n').slice(1).join('\r\n\r\n').replace(/\r\n$/, ''), 'latin1');
      const fileName = filePart.match(/filename="([^"]+)"/)?.[1], contentType = filePart.match(/Content-Type: ([^\r]+)/i)?.[1];
      const existing = rows.find(f => f.id === id); if (existing) { const { data, ...attachment } = existing; return send(201, { attachment, revision: ticket.revision, created: false }); }
      if (revision !== ticket.revision) return send(409, {});
      if (!content.length || content.length > 5242880 || !['image/png', 'image/jpeg', 'application/pdf', 'text/plain'].includes(contentType)) return send(400, { errors: { attachments: ['Invalid attachment.'] } });
      const saved = { id, ticketId: ticket.id, fileName, contentType, length: content.length, sha256: require('crypto').createHash('sha256').update(content).digest('hex'), createdAt: new Date().toISOString(), data: content };
      rows.push(saved); attachments.set(ticket.id, rows); ticket.revision++; const { data, ...attachment } = saved; return send(201, { attachment, revision: ticket.revision, created: true });
    }
  }
  if (url.pathname.startsWith('/api/predictions/') && req.method === 'POST') {
    await body(); await new Promise(resolve => setTimeout(resolve, 250));
    if (failureMode === 'prediction-unavailable') return send(503, {});
    return send(200, url.pathname.endsWith('category') ? { category: 'Database', confidence: .94, modelVersion: 'category-fixture' } : url.pathname.endsWith('priority') ? { predictedPriority: 'High', confidence: .91, modelVersion: 'priority-fixture' } : { team: 'Database', confidence: .88, needsHumanReview: false, threshold: .8, modelVersion: 'team-fixture' });
  }
  if (url.pathname === '/api/tickets' && req.method === 'POST') {
    const input = await body();
    if (!input.title?.trim() || !input.description?.trim()) return send(400, { errors: { title: ['Enter the required ticket details.'] } });
    if (input.createdById !== '20000000-0000-4000-8000-000000000001') return send(404, { title: 'Active user not found' });
    if (failureMode === 'create-unavailable') return send(503, {});
    const now = new Date().toISOString(); const ticket = { ...seedTickets[0], id: require('crypto').randomUUID(), reference: 'IT-' + (2000 + tickets.length), title: input.title.trim(), description: input.description.trim(), category: input.category, priority: input.priority, assignedTeamId: input.assignedTeamId, status: 'Open', revision: 1, createdAt: now, updatedAt: now, commentCount: 0 };
    tickets.unshift(ticket); details.set(ticket.id, { resolution: null, resolvedAt: null, closedAt: null, comments: [], predictions: [] }); predict(ticket); return send(201, detail(ticket));
  }
  if (/^\/api\/tickets\/[^/]+$/.test(url.pathname) && req.method === 'PUT') {
    const input = await body(), ticket = tickets.find(t => t.id === url.pathname.split('/').pop()); if (!ticket) return send(404, {});
    if (failureMode === 'conflict') { failureMode = ''; ticket.revision++; return send(409, {}); }
    if (input.revision !== ticket.revision) return send(409, {});
    Object.assign(ticket, { title: input.title, description: input.description, category: input.category, priority: input.priority, ...(input.assignment ? { assignedTeamId: input.assignment.teamId } : {}), revision: ticket.revision + 1 }); return send(200, detail(ticket));
  }
  const operation = url.pathname.match(/^\/api\/tickets\/([^/]+)\/(analyze|investigate|assignment|status|comments)$/);
  if (operation && req.method !== 'GET') {
    const ticket = tickets.find(t => t.id === operation[1]);
    if (!ticket) return send(404, { title: 'Ticket not found' });
    const action = operation[2], input = await body();
    if (ticket.isArchived || ticket.status === 'Closed') return send(409, { title: 'Ticket is not active' });
    if (action === 'analyze' || action === 'investigate') {
      if (req.method !== 'POST') return send(405, { title: 'POST required' });
      await new Promise(resolve => setTimeout(resolve, 200));
      const predictions = predict(ticket);
      if (action === 'analyze') return send(200, predictions);
      if (failureMode === 'llm-unavailable') return send(503, { title: 'Synthetic provider unavailable' });
      const result = { runId: require('crypto').randomUUID(), ticketRevision: ticket.revision,
        facts: { id: ticket.id, revision: ticket.revision, title: ticket.title, description: ticket.description, status: ticket.status, isArchived: ticket.isArchived, assignedTeamId: ticket.assignedTeamId },
        predictions, summary: 'The reported SQL timeout needs a connection-path investigation.', diagnosis: 'Hypothesis: the application cannot reach the configured SQL listener.',
        possibleCauses: ['Hypothesis: a firewall change blocks TCP 1433.', 'Hypothesis: the configured SQL host is incorrect.'],
        recommendedActions: [{ description: 'Ask an operator to verify the SQL host and TCP port 1433.', requiresHumanApproval: true }],
        sources: [{ documentId: documents[0].id, chunkId: '60000000-0000-4000-8000-000000000001', documentVersion: 1, title: documents[0].title, source: documents[0].source, contentHash: 'a'.repeat(64) }],
        confidence: .88, requiresHumanReview: true };
      analyses.set(ticket.id, { result, createdAt: new Date().toISOString() }); return send(200, result);
    }
    if (failureMode === 'conflict') { failureMode = ''; ticket.revision++; ticket.title = 'Updated by another support agent'; return send(409, { title: 'Revision conflict' }); }
    if (input.revision !== ticket.revision) return send(409, { title: 'Revision conflict' });
    const stored = details.get(ticket.id) || (detail(ticket), details.get(ticket.id));
    if (action === 'assignment') {
      if (req.method !== 'PATCH') return send(405, {});
      if (input.teamId !== null && !teams.some(t => t.id === input.teamId)) return send(404, { title: 'Team not found' });
      ticket.assignedTeamId = input.teamId;
    } else if (action === 'status') {
      if (req.method !== 'PATCH') return send(405, {});
      const allowed = { Open: ['InProgress'], InProgress: ['Resolved'], Resolved: ['InProgress', 'Closed'] };
      if (!allowed[ticket.status]?.includes(input.status)) return send(409, { title: 'Transition denied' });
      if (input.status === 'Resolved' && !input.resolution?.trim()) return send(400, { title: 'Resolution required' });
      ticket.status = input.status; stored.resolution = input.resolution;
    } else if (action === 'comments') {
      if (req.method !== 'POST' || input.authorId !== ticket.createdById || !input.message?.trim()) return send(400, { title: 'Valid author and message required' });
      const comment = { id: require('crypto').randomUUID(), ticketId: ticket.id, authorId: input.authorId, message: input.message, createdAt: new Date().toISOString() };
      stored.comments.push(comment); ticket.revision++; ticket.commentCount++; ticket.updatedAt = comment.createdAt;
      return send(201, { comment, ticketRevision: ticket.revision });
    }
    ticket.revision++; ticket.updatedAt = new Date().toISOString(); return send(200, detail(ticket));
  }
  if (req.method !== 'GET') return send(501, { title: 'Endpoint outside the frontend contract' });
  const search = url.searchParams.get('search') || '';
  if (search === 'simulate-unavailable') return send(503, { title: 'Synthetic unavailable API', traceId: 'fixture-503' });
  let items;
  if (url.pathname === '/api/tickets') {
    items = tickets.filter(t => (!search || `${t.title} ${t.description}`.toLowerCase().includes(search.toLowerCase())) &&
      (!url.searchParams.has('status') || t.status === url.searchParams.get('status')) &&
      (!url.searchParams.has('priority') || t.priority === url.searchParams.get('priority')));
  } else if (url.pathname.match(/^\/api\/tickets\/[^/]+\/workspace$/)) {
    const ticket = tickets.find(t => t.id === url.pathname.split('/')[3]);
    return ticket ? send(200, { assignedTeam: teams.find(t => t.id === ticket.assignedTeamId) || null, assignableTeams: teams, teamsTruncated: false, analysis: analyses.get(ticket.id) || null }) : send(404, { title: 'Ticket not found' });
  } else if (url.pathname.startsWith('/api/tickets/')) {
    const ticket = tickets.find(t => t.id === url.pathname.split('/').pop());
    return ticket ? send(200, detail(ticket)) : send(404, { title: 'Ticket not found' });
  } else if (url.pathname === '/api/knowledge') {
    items = documents.filter(d => !search || d.title.toLowerCase().includes(search.toLowerCase()));
  } else if (url.pathname.startsWith('/api/knowledge/')) {
    const document = documents.find(d => d.id === url.pathname.split('/').pop());
    if (!document) return send(404, { title: 'Document not found' });
    const { chunkCount, ...details } = document;
    return send(200, { ...details, createdById: null, createdAt: document.updatedAt, content: 'Check the connection path\n\nVerify the host name, port and authentication settings. Capture the observed error before proposing a configuration change.\n\nEscalate to the owning team with the findings.', cleanedContent: null, processedAt: null, processingVersion: null, chunks: [] });
  } else return send(501, { title: 'Endpoint outside the read-only frontend contract' });
  const page = Math.max(1, Number(url.searchParams.get('page')) || 1), pageSize = Math.max(1, Math.min(100, Number(url.searchParams.get('pageSize')) || 20));
  send(200, { items: items.slice((page - 1) * pageSize, page * pageSize), totalCount: items.length, page, pageSize });
}).listen(process.env.PORT || 4907);
