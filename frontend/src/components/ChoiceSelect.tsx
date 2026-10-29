import { ChevronDown, Check } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
export function ChoiceSelect({ label, value, options, onChange, error }: { label: string; value: string; options: { value: string; label: string }[]; onChange: (value: string) => void; error?: string }) {
  const id = useId(), root = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false), [active, setActive] = useState(0);
  const typeahead = useRef({ value: '', time: 0 });
  const selected = options.findIndex(o => o.value === value);
  useEffect(() => { if (open) document.getElementById(`${id}-${active}`)?.scrollIntoView({ block: 'nearest' }); }, [active, open, id]);
  useEffect(() => { if (!open) return; const close = (e: PointerEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); }; document.addEventListener('pointerdown', close); return () => document.removeEventListener('pointerdown', close); }, [open]);
  function choose(index: number) { if (options[index]) onChange(options[index].value); setOpen(false); }
  return <div className="form-field choice-select" ref={root} onBlur={e => { if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false); }}><label id={`${id}-label`} htmlFor={`${id}-button`}>{label}</label><button id={`${id}-button`} type="button" role="combobox" aria-labelledby={`${id}-label ${id}-value`} aria-haspopup="listbox" aria-expanded={open} aria-controls={`${id}-list`} aria-activedescendant={open ? `${id}-${active}` : undefined} aria-invalid={Boolean(error)} aria-describedby={error ? `${id}-error` : undefined} onClick={() => { setActive(Math.max(0, selected)); setOpen(!open); }} onKeyDown={e => {
    if (e.key === 'Escape') { e.preventDefault(); setOpen(false); return; }
    if (e.key === 'Tab') { setOpen(false); return; }
    if (['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' '].includes(e.key)) {
      e.preventDefault(); if ((e.key === 'Enter' || e.key === ' ') && open) return choose(active);
      const index = !open ? Math.max(0, selected) : e.key === 'ArrowDown' ? Math.min(options.length - 1, active + 1) : e.key === 'ArrowUp' ? Math.max(0, active - 1) : e.key === 'Home' ? 0 : e.key === 'End' ? options.length - 1 : active;
      setActive(index); setOpen(true);
    } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
      const now = performance.now(); typeahead.current = { value: (now - typeahead.current.time < 700 ? typeahead.current.value : '') + e.key.toLowerCase(), time: now };
      const found = options.findIndex(o => o.label.toLowerCase().startsWith(typeahead.current.value)); if (found >= 0) { setActive(found); setOpen(true); }
    }
  }}><span id={`${id}-value`}>{options[selected]?.label ?? 'Current team unavailable'}</span><ChevronDown size={16} aria-hidden="true" /></button>{open && <ul id={`${id}-list`} role="listbox" aria-labelledby={`${id}-label`}>{options.map((option, index) => <li id={`${id}-${index}`} role="option" aria-selected={value === option.value} className={active === index ? 'active' : ''} key={option.value} onPointerMove={() => setActive(index)} onMouseDown={e => e.preventDefault()} onClick={() => choose(index)}><span>{option.label}</span>{value === option.value && <Check size={16} aria-hidden="true" />}</li>)}</ul>}{error && <p id={`${id}-error`} className="error-text">{error}</p>}</div>;
}
