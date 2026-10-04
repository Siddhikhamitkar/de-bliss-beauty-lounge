import { describe, expect, it } from 'vitest'
import { greetingFor, relativeSince, relativeUntil } from './dashboardTime.ts'

describe('greetingFor', () => {
  it('greets by hour of day', () => {
    expect(greetingFor(6)).toBe('Good morning')
    expect(greetingFor(11)).toBe('Good morning')
    expect(greetingFor(12)).toBe('Good afternoon')
    expect(greetingFor(16)).toBe('Good afternoon')
    expect(greetingFor(17)).toBe('Good evening')
    expect(greetingFor(22)).toBe('Good evening')
    expect(greetingFor(2)).toBe('Good evening')
  })
})

describe('relativeUntil', () => {
  const now = new Date('2026-10-03T10:00:00')

  it('labels past-due appointments as due now', () => {
    expect(relativeUntil('2026-10-03T09:59:00', now)).toBe('due now')
    expect(relativeUntil('2026-10-03T10:00:00', now)).toBe('due now')
  })

  it('labels sub-hour gaps in minutes', () => {
    expect(relativeUntil('2026-10-03T10:30:00', now)).toBe('in 30 min')
  })

  it('labels hour-plus gaps in hours', () => {
    expect(relativeUntil('2026-10-03T12:00:00', now)).toBe('in 2 hr')
  })
})

describe('relativeSince', () => {
  const now = new Date('2026-10-03T10:00:00')

  it('labels fresh notes as just now', () => {
    expect(relativeSince('2026-10-03T09:59:50', now)).toBe('just now')
  })

  it('labels minutes, hours and days', () => {
    expect(relativeSince('2026-10-03T09:30:00', now)).toBe('30 min ago')
    expect(relativeSince('2026-10-03T08:00:00', now)).toBe('2 hr ago')
    expect(relativeSince('2026-09-30T10:00:00', now)).toBe('3 d ago')
  })
})
