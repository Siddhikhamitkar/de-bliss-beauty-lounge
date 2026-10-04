/** Draft and Paid reuse the appointment status tones; the key must match the backend enum name. */
export function InvoiceStatusPill({ status }: { status: string }) {
  return <span className={`pill status-pill status-${status}`}>{status}</span>
}
