import { useCallback, useEffect, useState } from 'react';
export function useResource<T>(loader: (signal: AbortSignal) => Promise<T>, keepPreviousData = false) {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<{ data?: T; error?: Error; loading: boolean }>({ loading: true });
  useEffect(() => {
    const controller = new AbortController();
    setState(previous => ({ data: keepPreviousData ? previous.data : undefined, loading: true }));
    loader(controller.signal).then(data => { if (!controller.signal.aborted) setState({ data, loading: false }); })
      .catch((error: unknown) => { if (!controller.signal.aborted) setState(previous => ({ data: keepPreviousData ? previous.data : undefined, error: error instanceof Error ? error : new Error('Unable to load this view.'), loading: false })); });
    return () => controller.abort();
  }, [loader, attempt, keepPreviousData]);
  const reload = useCallback(() => setAttempt(value => value + 1), []);
  return { ...state, reload };
}
