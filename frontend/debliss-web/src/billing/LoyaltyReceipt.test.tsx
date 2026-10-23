import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { Receipt } from '../api/contracts.ts'
import { ReceiptView } from './ReceiptView.tsx'
import { discountedTotal } from './totals.ts'

const receipt: Receipt = {
  salonName: 'Test salon', invoiceId: 'inv1', invoiceNumber: 'INV-1', customerName: 'Customer', items: [],
  currency: 'AED', subTotal: 200, bundleDiscount: 20, couponCode: 'SAVE', couponDiscount: 10,
  manualDiscount: 5, loyaltyDiscount: 15, loyaltyPointsRedeemed: 15, loyaltyPointsEarned: 7.5,
  taxRate: 5, pricesIncludeTax: false, taxTotal: 7.5, tipAmount: 10, totalAmount: 167.5,
  paymentMethod: 'Cash', paymentReference: null, paidAt: '2026-10-01T10:00:00Z', issuedAt: '2026-10-01T10:00:00Z',
}

describe('loyalty receipt arithmetic', () => {
  it('subtracts loyalty exactly once with bundle, coupon and manual discounts', () => {
    expect(discountedTotal(receipt)).toBe(150)
    render(<ReceiptView receipt={receipt} />)
    expect(screen.getByText('Loyalty discount').nextElementSibling).toHaveTextContent('−AED 15.00')
    expect(screen.getByText('Total before VAT').nextElementSibling).toHaveTextContent('AED 150.00')
    expect(screen.getByText('VAT (5%)').nextElementSibling).toHaveTextContent('+AED 7.50')
    expect(screen.getByText('Tip').nextElementSibling).toHaveTextContent('+AED 10.00')
    expect(screen.getByText('Total').nextElementSibling).toHaveTextContent('AED 167.50')
    expect(screen.getByText('Loyalty points earned').nextElementSibling).toHaveTextContent('7.50')
  })
  it('does not add tax again for inclusive prices or deduct loyalty from stored payable', () => {
    render(<ReceiptView receipt={{ ...receipt, pricesIncludeTax: true, taxTotal: 7.14, totalAmount: 160 }} />)
    expect(screen.getByText('Total (VAT included)').nextElementSibling).toHaveTextContent('AED 150.00')
    expect(screen.getByText('Includes VAT (5%)').nextElementSibling).toHaveTextContent('AED 7.14')
    expect(screen.getByText('Total').nextElementSibling).toHaveTextContent('AED 160.00')
  })
  it('retains two-decimal arithmetic and no-loyalty totals', () => {
    expect(discountedTotal({ ...receipt, subTotal: 10.1, bundleDiscount: 0, couponDiscount: 0, manualDiscount: 0, loyaltyDiscount: 0.2 })).toBe(9.9)
    expect(discountedTotal({ ...receipt, loyaltyDiscount: 0 })).toBe(165)
  })
})
