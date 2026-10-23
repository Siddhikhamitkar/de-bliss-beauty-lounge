import { afterEach, describe, expect, it, vi } from 'vitest'
import { checkoutInvoice, getCheckoutPreview } from './billing.ts'
import type { CheckoutInvoiceRequest, LoyaltySettings } from './contracts.ts'
import { getCustomerLoyalty, getLoyaltySettings, getLoyaltyTransactions, updateLoyaltySettings } from './loyalty.ts'

const settings: LoyaltySettings = { isEnabled: false, earningPercentage: 5, pointValueAed: 1, minimumRedemptionPoints: 0, maximumRedemptionPercentage: 50, allowPartialRedemption: true, minimumInvoiceAmount: null }
const checkout: CheckoutInvoiceRequest = { couponCode: null, manualDiscountType: null, manualDiscountValue: null, paymentMethod: 'Cash', amount: 100, reference: null, tipPercentage: null, tipAmount: null, loyaltyPointsToRedeem: 20 }
afterEach(() => vi.unstubAllGlobals())

describe('loyalty API contracts', () => {
  it('uses the owner settings and read-only customer endpoints with paging', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response('{}'))
    vi.stubGlobal('fetch', fetchMock)
    await getLoyaltySettings()
    expect(fetchMock).toHaveBeenLastCalledWith('/api/loyalty/settings', expect.objectContaining({ method: 'GET', credentials: 'same-origin' }))
    await updateLoyaltySettings(settings)
    expect(fetchMock).toHaveBeenLastCalledWith('/api/loyalty/settings', expect.objectContaining({ method: 'PUT', body: JSON.stringify(settings) }))
    await getCustomerLoyalty('c1')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/customers/c1/loyalty', expect.objectContaining({ method: 'GET' }))
    await getLoyaltyTransactions('c1', 2)
    expect(fetchMock).toHaveBeenLastCalledWith('/api/customers/c1/loyalty/transactions?page=2&pageSize=50', expect.objectContaining({ method: 'GET' }))
  })
  it('posts the same optional redemption field to preview and checkout, without settings calls', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response('{}'))
    vi.stubGlobal('fetch', fetchMock)
    await getCheckoutPreview('inv1', checkout)
    expect(fetchMock).toHaveBeenLastCalledWith('/api/invoices/inv1/checkout-preview', expect.objectContaining({ method: 'POST', body: JSON.stringify(checkout) }))
    await checkoutInvoice('inv1', checkout)
    expect(fetchMock).toHaveBeenLastCalledWith('/api/invoices/inv1/checkout', expect.objectContaining({ method: 'POST', body: JSON.stringify(checkout) }))
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
  it.each([400, 403, 409])('preserves %s errors for validation, authorization and conflicts', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { earningPercentage: ['Review this rule.'] } }), { status })))
    await expect(updateLoyaltySettings(settings)).rejects.toMatchObject({ status, errors: { earningPercentage: ['Review this rule.'] } })
  })
})
