import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import type { LoyaltySettings } from '../api/contracts.ts'
import { getLoyaltySettings, updateLoyaltySettings } from '../api/loyalty.ts'
import { LoyaltySettingsPage } from './LoyaltySettingsPage.tsx'

vi.mock('../api/loyalty.ts', () => ({ getLoyaltySettings: vi.fn(), updateLoyaltySettings: vi.fn() }))
const defaults: LoyaltySettings = { isEnabled: false, earningPercentage: 5, pointValueAed: 1, minimumRedemptionPoints: 0, maximumRedemptionPercentage: 50, allowPartialRedemption: true, minimumInvoiceAmount: null }

beforeEach(() => {
  vi.mocked(getLoyaltySettings).mockResolvedValue(defaults)
  vi.mocked(updateLoyaltySettings).mockResolvedValue(defaults)
})

describe('Loyalty settings', () => {
  it('loads server defaults and explains rounding and full-balance-only limits', async () => {
    render(<LoyaltySettingsPage />)
    expect(await screen.findByLabelText('Earning percentage (%)')).toHaveValue(5)
    expect(screen.getByLabelText('Enable loyalty program')).not.toBeChecked()
    expect(screen.getByLabelText('Value of one point (AED)')).toHaveValue(1)
    expect(screen.getByLabelText('Minimum redemption points')).toHaveValue(0)
    expect(screen.getByLabelText('Maximum redemption (% of eligible invoice)')).toHaveValue(50)
    expect(screen.getByLabelText('Allow partial redemption')).toBeChecked()
    expect(screen.getByText(/rounded to 2 decimal places/)).toBeInTheDocument()
    expect(screen.getByText(/FULL available balance/)).toBeInTheDocument()
  })

  it('saves every rule with an optional invoice minimum', async () => {
    const user = userEvent.setup()
    render(<LoyaltySettingsPage />)
    await user.click(await screen.findByLabelText('Enable loyalty program'))
    await user.clear(screen.getByLabelText('Earning percentage (%)'))
    await user.type(screen.getByLabelText('Earning percentage (%)'), '7.5')
    await user.type(screen.getByLabelText('Minimum invoice amount (AED, optional)'), '100')
    await user.click(screen.getByLabelText('Allow partial redemption'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(updateLoyaltySettings).toHaveBeenCalledExactlyOnceWith({ ...defaults, isEnabled: true, earningPercentage: 7.5, minimumInvoiceAmount: 100, allowPartialRedemption: false })
    expect(await screen.findByText('Saved — new checkouts use these loyalty rules.')).toBeInTheDocument()
  })

  it('sends null for an unset minimum and surfaces API field validation', async () => {
    vi.mocked(updateLoyaltySettings).mockRejectedValue(new ApiError(400, { errors: { pointValueAed: ['Point value must be positive.'], earningPercentage: ['Earning percentage cannot exceed 100.'] } }))
    const user = userEvent.setup()
    render(<LoyaltySettingsPage />)
    await user.clear(await screen.findByLabelText('Value of one point (AED)'))
    await user.type(screen.getByLabelText('Value of one point (AED)'), '0')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(updateLoyaltySettings).toHaveBeenCalledWith({ ...defaults, pointValueAed: 0 })
    expect(screen.getAllByText('Point value must be positive.').length).toBeGreaterThan(0)
    expect(screen.getByText('Earning percentage cannot exceed 100.')).toBeInTheDocument()
  })

  it('does not turn an empty required field into zero', async () => {
    const user = userEvent.setup()
    render(<LoyaltySettingsPage />)
    await user.clear(await screen.findByLabelText('Earning percentage (%)'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(screen.getByText('Earning percentage (%) is required.')).toBeInTheDocument()
    expect(updateLoyaltySettings).not.toHaveBeenCalled()
  })

  it('retries failed loads and surfaces permission failures', async () => {
    vi.mocked(getLoyaltySettings).mockRejectedValueOnce(new ApiError(403, { detail: 'Owner access required.' }))
    render(<LoyaltySettingsPage />)
    expect(await screen.findByText('Owner access required.')).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByLabelText('Earning percentage (%)')).toHaveValue(5)
  })
})
