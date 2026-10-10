/** In stock, low and out reuse the shared status-pill hues; the key must match the backend's StockStatus name. */
export function StockStatusPill({ status }: { status: string }) {
  const label =
    status === 'InStock' ? 'In stock' : status === 'LowStock' ? 'Low stock' : 'Out of stock'

  return <span className={`pill status-pill status-${status}`}>{label}</span>
}
