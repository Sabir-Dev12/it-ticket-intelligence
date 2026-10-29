import { useEffect, useId, useRef } from 'react';
import { Button } from './ui';
export function ConfirmDialog({ title, children, confirmLabel, onConfirm, onCancel, pending = false }: { title: string; children: React.ReactNode; confirmLabel: string; onConfirm: () => void; onCancel: () => void; pending?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null), titleId = useId();
  useEffect(() => { const previous = document.activeElement as HTMLElement | null; ref.current?.showModal(); return () => { ref.current?.close(); previous?.focus(); }; }, []);
  return <dialog ref={ref} className="confirm-dialog" aria-labelledby={titleId} onKeyDown={e => {
    if (e.key !== 'Tab') return;
    const controls = [...e.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled),a[href],input:not(:disabled),[tabindex="0"]')];
    const first = controls[0], last = controls.at(-1);
    if (!first) { e.preventDefault(); return; }
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus(); }
    if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  }} onCancel={e => { e.preventDefault(); if (!pending) onCancel(); }}><h2 id={titleId}>{title}</h2><div>{children}</div><div className="form-actions"><Button autoFocus onClick={onCancel} disabled={pending}>Keep editing</Button><Button className="danger" disabled={pending} onClick={onConfirm}>{confirmLabel}</Button></div></dialog>;
}
