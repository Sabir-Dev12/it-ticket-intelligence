import '../styles/knowledge.css';
import { useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { knowledgeApi, knowledgeSchema, documentKinds, documentFormats, importKnowledgeText, type KnowledgeValues } from '../api/knowledge';
import { ApiError } from '../api/client';
import { useWorkspace } from '../app/Workspace';
import { Button, PageHeader } from '../components/ui';
import { ChoiceSelect } from '../components/ChoiceSelect';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useUnsavedChanges } from './ticket-form/useUnsavedChanges';
import { categories } from './ticket-form/schema';
const empty:KnowledgeValues={title:'',content:'',source:'',category:'Other',kind:'Document',format:'PlainText'};
export default function CreateKnowledge(){
  const {user}=useWorkspace();const navigate=useNavigate();
  const [values,setValues]=useState(empty),[errors,setErrors]=useState<Record<string,string>>({}),[error,setError]=useState(''),[pending,setPending]=useState(false),[index,setIndex]=useState(true),[unknown,setUnknown]=useState(false),[importing,setImporting]=useState(false);
  const busy=useRef(false);const {blocker,allow}=useUnsavedChanges(JSON.stringify(values)!==JSON.stringify(empty)||pending);
  async function save(event:React.FormEvent){event.preventDefault();if(busy.current||unknown||importing||!user)return;
    const parsed=knowledgeSchema.safeParse(values);if(!parsed.success){const issues:Record<string,string>={};for(const issue of parsed.error.issues)issues[String(issue.path[0])]??=issue.message;setErrors(issues);setError('Review the highlighted fields.');requestAnimationFrame(()=>document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());return;}
    busy.current=true;setPending(true);setError('');setErrors({});
    try{const doc=await knowledgeApi.create(parsed.data,user.id);let notice='Document saved and available in the library.';
      if(index){try{await knowledgeApi.index(doc.id);notice+=' AI indexing requested in the background. Search availability depends on its completion.';}catch{notice+=' AI indexing could not be requested. The document is still saved.';}}
      allow.current=true;navigate(`/knowledge/${doc.id}`,{state:{knowledgeNotice:notice}});
    }catch(e){if(e instanceof ApiError&&(e.status===0||e.status===408||e.status>=500))setUnknown(true);setError(e instanceof ApiError?e.validation??(e.status===409?'A document with this source may already exist. Use a unique source reference or check the library.':e.message):'The document could not be saved.');}
    finally{busy.current=false;setPending(false);}
  }
  return <><Link className="back-link" to="/knowledge">Back to knowledge</Link><PageHeader eyebrow="KNOWLEDGE BASE" title="Add a document." description="Publish an article, guide, FAQ or procedure for your support team."/>
    <form className="panel ticket-editor knowledge-editor" onSubmit={event=>void save(event)} noValidate aria-busy={pending||importing}>
      <fieldset className="ticket-form-fields" disabled={pending||importing||unknown}>
        <div className="form-field"><label htmlFor="knowledge-file">Import text file (optional)</label><input id="knowledge-file" type="file" accept=".txt,.md,text/plain,text/markdown" aria-describedby="knowledge-file-help" onChange={async event=>{const file=event.target.files?.[0];event.target.value='';if(!file)return;setImporting(true);setError('');setErrors({});try{const imported=await importKnowledgeText(file);setValues(old=>({...old,...imported,title:old.title||file.name.replace(/\.[^.]+$/,''),source:old.source||file.name}));}catch(e){setError(e instanceof Error?e.message:'The file could not be read.');}finally{setImporting(false);}}}/><p className="field-help" id="knowledge-file-help">TXT or Markdown, UTF-8, up to 400 KB and 100,000 characters. Import replaces the content below. PDF and Word are not supported yet.</p></div>
        {(['title','source'] as const).map(field=><div className="form-field" key={field}><label htmlFor={`knowledge-${field}`}>{field==='title'?'Title':'Source'}</label><input id={`knowledge-${field}`} value={values[field]} maxLength={field==='title'?160:512} aria-required="true" aria-invalid={Boolean(errors[field])} aria-describedby={`${field}-knowledge-error`} onChange={event=>setValues({...values,[field]:event.target.value})}/><p className="error-text" id={`${field}-knowledge-error`}>{errors[field]}</p>{field==='source'&&<p className="field-help">Unique document reference or URL. The source is recorded, not downloaded.</p>}</div>)}
        <ChoiceSelect label="Category" value={values.category} options={categories.map(value=>({value,label:value}))} onChange={value=>setValues({...values,category:value as KnowledgeValues['category']})} error={errors.category}/>
        <ChoiceSelect label="Document type" value={values.kind} options={documentKinds.map(value=>({value,label:value.replace(/([a-z])([A-Z])/g,'$1 $2')}))} onChange={value=>setValues({...values,kind:value as KnowledgeValues['kind']})} error={errors.kind}/>
        <ChoiceSelect label="Content format" value={values.format} options={documentFormats.map(value=>({value,label:value==='PlainText'?'Plain text':'Markdown'}))} onChange={value=>setValues({...values,format:value as KnowledgeValues['format']})} error={errors.format}/>
        <div className="form-field"><label htmlFor="knowledge-content">Content</label><textarea id="knowledge-content" rows={14} value={values.content} maxLength={100000} aria-required="true" aria-invalid={Boolean(errors.content)} aria-describedby="knowledge-content-error" onChange={event=>setValues({...values,content:event.target.value})}/><p className="field-help">{values.content.length.toLocaleString()} / 100,000 characters. Content is displayed safely as text.</p><p className="error-text" id="knowledge-content-error">{errors.content}</p></div>
        <label className="knowledge-index-option"><input type="checkbox" checked={index} onChange={event=>setIndex(event.target.checked)}/> Prepare this document for AI search in the background</label>
      </fieldset>
      {error&&<p className="inline-error" role="alert">{error}</p>}{unknown&&<p className="notice" role="alert">The save outcome is unknown. Check the library before trying again to avoid duplicate documents.</p>}
      <div className="form-actions"><Button type="submit" className="primary" loading={pending} disabled={importing||unknown}>Save document</Button><Button type="button" disabled={pending||importing} onClick={()=>navigate('/knowledge')}>Cancel</Button></div>
    </form>{blocker.state==='blocked'&&<ConfirmDialog title="Leave this form?" confirmLabel="Discard unsaved changes" pending={pending} onConfirm={()=>blocker.proceed()} onCancel={()=>blocker.reset()}><p>Your unsaved document content will be discarded.</p></ConfirmDialog>}</>;
}



