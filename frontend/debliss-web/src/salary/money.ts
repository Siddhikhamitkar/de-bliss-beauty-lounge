/** Money is validated and added in integer fils, never via floating-point sums. */
export const MAX_MONEY_CENTS = 99_999_999_999

export function parseCents(raw: string): number | undefined {
  const text = raw.trim()
  if (!/^\d+(?:\.\d{1,2})?$/.test(text)) return undefined
  const [whole, fraction = ''] = text.split('.')
  const cents = Number(whole) * 100 + Number(fraction.padEnd(2, '0'))
  return Number.isSafeInteger(cents) && cents <= MAX_MONEY_CENTS ? cents : undefined
}

export const MONEY_ERROR = 'Enter an AED amount from 0 to 999,999,999.99 with at most 2 decimal places.'

export function moneyFromCents(cents: number): string {
  return `AED ${(cents / 100).toLocaleString('en-AE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

export function money(value: number | null): string {
  return value === null ? 'Not set' : moneyFromCents(Math.round(value * 100))
}

export function maskBankNumber(value: string | null): string {
  if (!value) return 'Not set'
  return value.length <= 4 ? '••••' : `•••• ${value.slice(-4)}`
}
