const price = new Intl.NumberFormat(undefined, {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

/**
 * Prices arrive as JSON numbers and are stored as decimal(18,2), so two places
 * is what the column holds. No currency symbol: the service catalogue does not
 * carry one, and inventing a default here would quietly assert a locale.
 */
export function formatPrice(value: number): string {
  return price.format(value)
}

/** Reads better on a salon menu than "90 min". */
export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const remainder = minutes % 60

  if (hours === 0) {
    return `${remainder} min`
  }

  if (remainder === 0) {
    return `${hours} hr`
  }

  return `${hours} hr ${remainder} min`
}

/** A timestamp rendered in the browser's local time, e.g. "6 Oct 2026, 09:00". */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}
