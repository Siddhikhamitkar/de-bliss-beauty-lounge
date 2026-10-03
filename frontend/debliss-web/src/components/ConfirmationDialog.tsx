import type { ReactNode } from 'react'

interface ConfirmationDialogProps {
  title: string
  children: ReactNode
  confirmLabel: string
  onConfirm: () => void
  onCancel: () => void
  busy?: boolean
}

/**
 * Asks for one explicit decision before an action that is awkward to undo,
 * such as removing a future time-off entry. Cancel is the safe default and
 * takes focus, so Enter and a hasty click both back out.
 */
export function ConfirmationDialog({
  title,
  children,
  confirmLabel,
  onConfirm,
  onCancel,
  busy = false,
}: ConfirmationDialogProps) {
  return (
    <div className="dialog-backdrop" onClick={onCancel}>
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirmation-dialog-title"
        onClick={event => event.stopPropagation()}
      >
        <h3 id="confirmation-dialog-title">{title}</h3>
        <div className="dialog-body">{children}</div>
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onCancel} disabled={busy} autoFocus>
            Cancel
          </button>
          <button
            type="button"
            className="button button-primary"
            onClick={onConfirm}
            disabled={busy}
          >
            {busy ? 'Working…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
