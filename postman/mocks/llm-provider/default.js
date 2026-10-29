// Contract fake only: no model inference and no outbound requests.
const http = require('http');
const counts = new Map();
http.createServer(async (req,res) => {
  const path = new URL(req.url,'http://local').pathname;
  const send=(status,body)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(body));};
  if(req.method==='GET' && path.endsWith('/api/tags'))return send(200,{models:[{name:'chat-sim'}]});
  const ollama = path.endsWith('/api/chat');
  if(req.method !== 'POST' || (!ollama && !path.endsWith('/chat/completions')))
    return send(501,{error:'Only the structured chat contract is simulated.'});
  let body=req.body;
  if(!body){let raw='';for await(const part of req)raw+=part;try{body=JSON.parse(raw);}catch{return send(400,{error:'Invalid JSON'});}}
  if(typeof body==='string'){try{body=JSON.parse(body);}catch{return send(400,{error:'Invalid JSON'});}}
  if(body.model==='chat-sim'){
    if(body.stream!==true || !Array.isArray(body.messages))return send(400,{error:'Expected streaming messages'});
    res.writeHead(200,{'Content-Type':'application/x-ndjson'});
    res.write(JSON.stringify({model:'chat-sim',message:{role:'assistant',content:'Local '},done:false})+'\n');
    return res.end(JSON.stringify({model:'chat-sim',message:{role:'assistant',content:'response.'},done:true,done_reason:'stop'})+'\n');
  }
  if(typeof body.model==='string' && body.model.startsWith('agent-'))return agentReply(body,ollama,path,send);
  const schema=ollama?body.format:body.response_format?.json_schema?.schema;
  if(body.stream!==false || body.tools || schema?.type!=='object' || schema.additionalProperties!==false ||
     !Array.isArray(body.messages) || body.messages[0]?.role!=='system' || body.messages[1]?.role!=='user')
    return send(400,{error:'Expected bounded non-streaming structured messages.'});
  let supplied;try{supplied=JSON.parse(body.messages[1].content);}catch{return send(400,{error:'Expected JSON prompt/context.'});}
  const mode=body.model;
  const key=path+'/'+mode;const count=(counts.get(key)||0)+1;counts.set(key,count);
  if(mode==='unavailable' || (mode==='retry-once' && count%2===1))return send(503,{error:'Simulated unavailability'});
  const answer={summary:'Check SQL connectivity.',suggestedSteps:['Verify TCP port 1433.'],
    citedChunkIds:(supplied.context||[]).slice(0,1).map(c=>c.chunkId),insufficientContext:!(supplied.context||[]).length};
  const content=JSON.stringify(answer);
  const reply=()=>send(200,ollama?{model:mode,done:true,done_reason:'stop',message:{role:'assistant',content}}:
    {model:mode,choices:[{finish_reason:'stop',message:{role:'assistant',content}}]});
  if(mode==='timeout')return setTimeout(reply,3000);
  reply();
}).listen(process.env.PORT||4904);

function agentReply(body,ollama,path,send){
  if(body.stream!==false || !Array.isArray(body.messages) || body.messages[0]?.role!=='system')
    return send(400,{error:'Expected bounded agent messages'});
  const final=!body.tools;
  const schema=ollama?body.format:body.response_format?.json_schema?.schema;
  if(final && (schema?.type!=='object' || schema.additionalProperties!==false || !schema.properties?.diagnosis))
    return send(400,{error:'Expected final agent JSON schema'});
  if(!final && (body.tools.length!==6 || schema))return send(400,{error:'Expected six read-only tools without a final schema'});
  const results=body.messages.filter(m=>m.role==='tool');
  if(results.some(m=>ollama?!m.tool_name:!m.tool_call_id))return send(400,{error:'Missing tool correlation'});
  const mode=body.model;const key=path+'/'+mode;const count=(counts.get(key)||0)+1;counts.set(key,count);
  if(mode==='agent-unavailable' || (mode==='agent-retry-once' && count%2===1))return send(503,{error:'Simulated agent unavailability'});
  let supplied;try{supplied=JSON.parse(body.messages.find(m=>m.role==='user').content);}catch{return send(400,{error:'Expected JSON observations'});}
  const calls=(!final && !results.length)||(final && mode==='agent-final-tool');
  let message;
  if(mode==='agent-refused')message={role:'assistant',content:'',refusal:'Synthetic refusal'};
  else if(calls)message={role:'assistant',content:'',tool_calls:[ollama?
    {type:'function',function:{name:'get_ticket_history',arguments:{}}}:
    {id:'call-history',type:'function',function:{name:'get_ticket_history',arguments:'{}'}}]};
  else {
    const chunks=supplied.retrievedContext||[];
    const answer={summary:'SQL timeout',diagnosis:'Connection path may be blocked',possibleCauses:['Firewall configuration'],
      recommendedActions:['Ask an operator to verify TCP 1433'],citedChunkIds:chunks.slice(0,1).map(c=>c.chunkId),confidence:.9,insufficientContext:!chunks.length};
    message={role:'assistant',content:!final?'Ready for final phase.':mode==='agent-malformed'?'{"summary":"missing fields"}':JSON.stringify(answer)};
  }
  const reply=()=>send(200,ollama?{model:mode,done:true,done_reason:mode==='agent-truncated'?'length':'stop',message}:
    {model:mode,choices:[{finish_reason:mode==='agent-truncated'?'length':calls?'tool_calls':'stop',message}]});
  if(mode==='agent-timeout')return setTimeout(reply,3000);
  reply();
}
