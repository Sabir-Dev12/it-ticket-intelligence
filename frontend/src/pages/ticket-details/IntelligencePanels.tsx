import { BrainCircuit, FileText, ShieldCheck } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useId } from 'react';
import type { Ticket, TicketWorkspace } from '../../api/contracts';
import { Button, date, SectionHeader } from '../../components/ui';
import { latestPredictions } from './model';

export function MlPanel({ ticket }: { ticket: Ticket }) {
  const rows = latestPredictions(ticket.predictions);
  return <section className="panel intelligence-panel" aria-labelledby="ml-heading">
    <div className="section-header"><div><h2 id="ml-heading"><BrainCircuit size={18} aria-hidden="true" />ML.NET Intelligence</h2><p>Latest saved prediction batch. Suggestions do not change ticket properties.</p></div><span className="badge">{ticket.predictions.length} saved predictions</span></div>
    {ticket.predictions.length === 0 ? <p className="section-empty">No model predictions have been saved for this ticket. Select Analyze Ticket to generate them.</p> : <dl className="intelligence-rows">{rows.map(({ type, prediction }) => {
      const label = type === 'TeamRouting' ? 'Recommended team' : type;
      const value = prediction?.predictedCategory ?? prediction?.predictedPriority ?? prediction?.recommendedTeamName;
      return <div className="intelligence-row" key={type}><dt>{label}</dt><dd className="intelligence-value"><strong>{value ?? (prediction ? 'No recommendation' : 'Not available in this batch')}</strong>{prediction && <small>{prediction.inputRevision !== ticket.revision ? `Previous ticket version · Revision ${prediction.inputRevision}` : `Ticket revision ${prediction.inputRevision}`}{prediction.needsHumanReview && ' · Human review required'}</small>}</dd><dd className="confidence-block">{prediction ? <><span><strong>{Math.round(prediction.confidence * 100)}%</strong> confidence</span><meter min={0} max={1} value={prediction.confidence} aria-label={`${label} confidence`} /><small>{prediction.modelVersion} · {date(prediction.createdAt, true)}</small></> : <span>Confidence unavailable</span>}</dd></div>;
    })}</dl>}
    <div className="panel-footer"><span>Confidence is model output, not a calibrated guarantee.</span><ShieldCheck size={17} aria-hidden="true" /></div>
  </section>;
}

export function AiPanel({ workspace, revision, loading, error, retry }: {
  workspace?: TicketWorkspace; revision: number; loading: boolean; error?: Error; retry: () => void;
}) {
  const saved = workspace?.analysis;
  const analysis = saved?.result;
  const heading = useId();
  return <section className="panel ai-analysis-panel" aria-labelledby={heading}><SectionHeader title="AI Analysis" description="An evidence-based proposal for human review. Diagnosis and causes are hypotheses." />
    <h2 className="sr-only" id={heading}>AI investigation result</h2>
    {error && <div className="inline-error" role="alert"><p>Saved analysis could not be refreshed. {error.message}</p><Button onClick={retry} loading={loading}>Retry analysis read</Button></div>}
    {!analysis && !error && <div className="ai-empty"><BrainCircuit size={28} aria-hidden="true" /><div><h3>{loading ? 'Loading saved analysis' : 'No AI investigation saved'}</h3><p>{loading ? 'Retrieving the latest investigation.' : 'Select Ask AI to investigate this ticket using predictions, knowledge and read-only tools.'}</p></div></div>}
    {analysis && saved && <>
      <div className="analysis-metadata"><span className="badge"><ShieldCheck size={14} aria-hidden="true" />Human review required</span><span>{Math.round(analysis.confidence * 100)}% confidence indicator</span><time dateTime={saved.createdAt}>{date(saved.createdAt, true)}</time></div>
      {analysis.ticketRevision !== revision && <div className="notice"><ShieldCheck size={18} aria-hidden="true" /><p>This analysis describes revision {analysis.ticketRevision}. The ticket is now revision {revision}; review or investigate again before acting.</p></div>}
      <div className="analysis-copy"><section><h3>Summary</h3><p>{analysis.summary}</p></section><section><h3>Diagnosis <span className="subtle-label">Hypothesis</span></h3><p>{analysis.diagnosis}</p></section><section><h3>Possible Causes</h3>{analysis.possibleCauses.length ? <ul>{analysis.possibleCauses.map((cause, index) => <li key={index}>{cause}</li>)}</ul> : <p>No supported cause was identified.</p>}</section><section><h3>Recommended Actions <span className="subtle-label">Proposals</span></h3>{analysis.recommendedActions.length ? <ol className="recommended-actions">{analysis.recommendedActions.map((action, index) => <li key={index}><p>{action.description}</p><small>Human approval required before execution</small></li>)}</ol> : <p>No action was proposed.</p>}</section></div>
      <div className="analysis-sources"><h3>Sources</h3><p className="muted">Saved citations from this investigation. Documents may have changed since the cited version.</p>{analysis.sources.length ? <ul>{analysis.sources.map(source => <li key={source.chunkId}><FileText size={18} aria-hidden="true" /><div><Link className="text-link" to={`/knowledge/${source.documentId}`}>{source.title}</Link><p>{source.source} · Version {source.documentVersion}</p><details className="technical-details"><summary>Citation identifiers</summary><small className="mono">Document {source.documentId}</small><small className="mono">Chunk {source.chunkId}</small></details></div></li>)}</ul> : <p>No verified source was cited. Treat this proposal as insufficiently grounded.</p>}</div>
      <div className="panel-footer"><span>Proposals only. No action has been executed.</span><details className="technical-details"><summary>Run details</summary><span className="mono">{analysis.runId}</span></details></div>
    </>}
  </section>;
}
