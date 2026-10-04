/**
 * Time-of-day greeting for the dashboard header. The app has no signed-in
 * user yet, so the greeting stays generic rather than inventing a name.
 */
export function greetingFor(hour: number): string {
  if (hour >= 5 && hour < 12) {
    return 'Good morning'
  }

  if (hour >= 12 && hour < 17) {
    return 'Good afternoon'
  }

  return 'Good evening'
}

/** "in 25 min" / "in 3 hr" — how far ahead a booking starts. */
export function relativeUntil(iso: string, now: Date): string {
  const minutes = Math.round((new Date(iso).getTime() - now.getTime()) / 60_000)

  if (minutes <= 0) {
    return 'due now'
  }

  if (minutes < 60) {
    return `in ${minutes} min`
  }

  return `in ${Math.round(minutes / 60)} hr`
}

/** "just now" / "20 min ago" / "3 hr ago" — how fresh a reception note is. */
export function relativeSince(iso: string, now: Date): string {
  const minutes = Math.round((now.getTime() - new Date(iso).getTime()) / 60_000)

  if (minutes < 1) {
    return 'just now'
  }

  if (minutes < 60) {
    return `${minutes} min ago`
  }

  const hours = Math.round(minutes / 60)

  if (hours < 24) {
    return `${hours} hr ago`
  }

  return `${Math.round(hours / 24)} d ago`
}
