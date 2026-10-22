import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { getTaxSettings, updateTaxSettings } from '../api/settings.ts'
import { SettingsPage } from './SettingsPage.tsx'

vi.mock('../api/settings.ts', () => ({
  getTaxSettings: vi.fn(),
  updateTaxSettings: vi.fn(),
}))

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.mocked(getTaxSettings).mockResolvedValue({ taxRate: 5, pricesIncludeTax: false })
    vi.mocked(updateTaxSettings).mockResolvedValue({ taxRate: 5, pricesIncludeTax: false })
  })

  it('loads the salon tax configuration into the form', async () => {
    render(<SettingsPage />)

    expect(screen.getByRole('status')).toHaveTextContent('Loading tax settings…')

    expect(await screen.findByLabelText('VAT rate (%)')).toHaveValue(5)
    expect(screen.getByRole('radio', { name: 'Added at checkout' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Included in prices' })).not.toBeChecked()
    expect(getTaxSettings).toHaveBeenCalledExactlyOnceWith()
  })

  it('saves the rate and pricing convention together', async () => {
    const user = userEvent.setup()
    render(<SettingsPage />)
    const rate = await screen.findByLabelText('VAT rate (%)')

    await user.clear(rate)
    await user.type(rate, '5.25')
    await user.click(screen.getByRole('radio', { name: 'Included in prices' }))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(updateTaxSettings).toHaveBeenCalledExactlyOnceWith({
      taxRate: 5.25,
      pricesIncludeTax: true,
    })
    expect(
      await screen.findByText('Saved — new checkouts use this configuration immediately.'),
    ).toBeInTheDocument()
  })

  it('lets an empty rate reach the server and shows its field error', async () => {
    const user = userEvent.setup()
    vi.mocked(updateTaxSettings).mockRejectedValue(
      new ApiError(400, { errors: { taxRate: ['Enter a VAT rate between 0 and 100.'] } }),
    )

    render(<SettingsPage />)
    await screen.findByLabelText('VAT rate (%)')

    await user.clear(screen.getByLabelText('VAT rate (%)'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(updateTaxSettings).toHaveBeenCalledExactlyOnceWith({
      taxRate: null,
      pricesIncludeTax: false,
    })
    expect(await screen.findByText('Enter a VAT rate between 0 and 100.')).toBeInTheDocument()
    expect(screen.queryByText(/Saved —/)).not.toBeInTheDocument()
  })

  it('shows the load failure with a working retry', async () => {
    vi.mocked(getTaxSettings)
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce({ taxRate: 5, pricesIncludeTax: false })

    render(<SettingsPage />)

    expect(await screen.findByText('boom')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByLabelText('VAT rate (%)')).toHaveValue(5)
  })
})
