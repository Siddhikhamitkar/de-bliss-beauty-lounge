import { describe, expect, it } from 'vitest'
import { formatDuration, formatPrice } from './format.ts'

describe('formatDuration', () => {
  it('renders minutes alone under an hour', () => {
    expect(formatDuration(45)).toBe('45 min')
  })

  it('renders whole hours without minutes', () => {
    expect(formatDuration(60)).toBe('1 hr')
    expect(formatDuration(120)).toBe('2 hr')
  })

  it('renders hours with a remainder', () => {
    expect(formatDuration(90)).toBe('1 hr 30 min')
  })
})

describe('formatPrice', () => {
  it('always shows two fraction digits and no currency symbol', () => {
    expect(formatPrice(35)).toBe('35.00')
    expect(formatPrice(28.5)).toBe('28.50')
  })
})
