/** The single loading idiom, announced politely to screen readers. */
export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <p className="empty" role="status">{label}</p>
  )
}
