import { useEffect, useRef } from 'react';
import { useBlocker } from 'react-router-dom';
export function useUnsavedChanges(dirty: boolean) {
  const allow = useRef(false);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => !allow.current && dirty && (currentLocation.pathname !== nextLocation.pathname || currentLocation.search !== nextLocation.search));
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => { if (dirty && !allow.current) { event.preventDefault(); event.returnValue = ''; } };
    window.addEventListener('beforeunload', warn); return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);
  return { blocker, allow };
}
