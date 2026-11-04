import { useCallback, useEffect, useState } from 'react';

export type LoadState<T> =
  | { status: 'loading' }
  | { status: 'error'; error: unknown }
  | { status: 'loaded'; value: T };

/**
 * Runs `load` when the component mounts and whenever `load` changes (memoize it with useCallback), aborting the
 * previous request. `retry` shows the loading state again and reruns it.
 */
export function useLoad<T>(load: (signal: AbortSignal) => Promise<T>) {
  const [state, setState] = useState<LoadState<T>>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setState({ status: 'loading' });
    load(controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) setState({ status: 'loaded', value });
      },
      (error: unknown) => {
        if (!controller.signal.aborted) setState({ status: 'error', error });
      },
    );
    return () => controller.abort();
  }, [load, attempt]);

  const retry = useCallback(() => setAttempt((current) => current + 1), []);

  return { state, retry };
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong.';
}
