import { useCallback, useEffect, useRef, useState } from 'react'
import { describeFailure } from './api/client.ts'

export interface AsyncState<T> {
  data: T | undefined
  failure: string | undefined
  reload: () => void
}

export function useAsync<T>(
  load: () => Promise<T>,
  dependencies: readonly unknown[],
): AsyncState<T> {
  const [data, setData] = useState<T>()
  const [failure, setFailure] = useState<string>()
  const [attempt, setAttempt] = useState(0)
  const [snapshot, setSnapshot] = useState<readonly unknown[]>(dependencies)

  const loadRef = useRef(load)

  // Keep the latest loader without listing it as an effect dependency: callers pass
  // an inline closure whose identity changes every render, which would otherwise
  // restart the request each time. No dependency array means this runs after every
  // commit, before the fetch effect below (effects run in declaration order).
  useEffect(() => {
    loadRef.current = load
  })

  // Compare element-wise during render and re-render with a fresh snapshot when the
  // caller's inputs change. That keeps the effect's dependency list a literal array
  // the linter can verify, instead of a spread the rule rejects as a complex
  // expression. A changed snapshot also resets any in-flight reload counter.
  if (!shallowEqual(snapshot, dependencies)) {
    setSnapshot(dependencies)
    setAttempt(0)
  }

  useEffect(() => {
    let ignore = false

    loadRef
      .current()
      .then(result => {
        if (!ignore) {
          setData(result)
          setFailure(undefined)
        }
      })
      .catch((error: unknown) => {
        if (!ignore) {
          setFailure(describeFailure(error))
        }
      })

    return () => {
      ignore = true
    }
  }, [attempt, snapshot])

  const reload = useCallback(() => setAttempt(current => current + 1), [])

  return { data, failure, reload }
}

function shallowEqual(left: readonly unknown[], right: readonly unknown[]): boolean {
  return (
    left.length === right.length && left.every((value, index) => Object.is(value, right[index]))
  )
}
