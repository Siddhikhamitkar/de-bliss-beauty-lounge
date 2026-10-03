interface ErrorStateProps {
  message: string
  /** Retries the failed load; omitted on pages with no obvious retry target. */
  onRetry?: () => void
}

/** A failure with an optional retry, instead of a dead banner. */
export function ErrorState({ message, onRetry }: ErrorStateProps) {
  return (
    <div className="state-error">
      <p className="banner" role="alert">{message}</p>
      {onRetry !== undefined && (
        <button type="button" className="button" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  )
}
