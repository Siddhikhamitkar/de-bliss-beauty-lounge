import type { ReactNode } from 'react'

/** The single empty-list idiom, so every page reads the same way. */
export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>
}
