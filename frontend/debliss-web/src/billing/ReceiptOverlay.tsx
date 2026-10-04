import type { Receipt } from '../api/contracts.ts'
import { ReceiptView } from './ReceiptView.tsx'

interface ReceiptOverlayProps {
  receipt: Receipt
  /** Done closes the overlay; in checkout it also returns to the calendar. */
  onDone: () => void
}

/** Full-screen receipt step with Print and Done; closes when the backdrop is clicked. */
export function ReceiptOverlay({ receipt, onDone }: ReceiptOverlayProps) {
  return (
    <div className="receipt-overlay" onClick={onDone}>
      <div
        className="receipt-panel"
        role="dialog"
        aria-modal="true"
        aria-label={`Receipt ${receipt.invoiceNumber}`}
        onClick={event => event.stopPropagation()}
      >
        <ReceiptView receipt={receipt} />
        <div className="receipt-actions">
          <button type="button" className="button" onClick={() => window.print()}>
            Print receipt
          </button>
          <button type="button" className="button button-primary" onClick={onDone}>
            Done
          </button>
        </div>
      </div>
    </div>
  )
}
