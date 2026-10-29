// Synthetic vectors exercise transport/failures only, never semantic quality.
const http = require('http');
const model = 'simulation-embedding';
const digest = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
let mode = 'valid';
http.createServer(async (req, res) => {
  const path = new URL(req.url, 'http://local').pathname;
  const send = (status, body, type = 'application/json') => {
    res.writeHead(status, { 'Content-Type': type });
    res.end(type === 'application/json' ? JSON.stringify(body) : body);
  };
  // @endpoint GET /api/tags
  if (req.method === 'GET' && path.endsWith('/api/tags'))
    return send(200, { models: [{ name: model, digest }] });
  if (req.method !== 'POST' || (!path.endsWith('/api/embed') && path !== '/control'))
    return send(501, { error: 'This caller must only use tags and embed.' });
  let body = req.body;
  if (!body) {
    let raw = ''; for await (const part of req) raw += part;
    try { body = JSON.parse(raw); } catch { return send(400, { error: 'Invalid JSON' }); }
  }
  if (typeof body === 'string') { try { body = JSON.parse(body); } catch { return send(400, {error:'Invalid JSON'}); } }
  if (path === '/control') {
    if (!['valid','error','invalid','timeout'].includes(body.mode)) return send(400,{error:'Unknown test mode'});
    mode = body.mode; return send(200, {mode});
  }
  if (body.model !== model || !Array.isArray(body.input) || !body.input.every(t => typeof t === 'string') || body.truncate !== false)
    return send(400, { error: 'Expected model, ordered input strings and truncate=false.' });
  // @endpoint POST /api/embed
  if (mode === 'error') return send(500, '<html>Provider unavailable</html>', 'text/html');
  if (mode === 'invalid') return send(200, { model, embeddings: body.input.map(() => [1, 0]) });
  if (mode === 'timeout') return setTimeout(() => send(200, { model, embeddings: body.input.map(() => [1, 0, 0]) }), 3000);
  return send(200, { model, embeddings: body.input.map(() => [1, 0, 0]) });
}).listen(process.env.PORT || 4902);
