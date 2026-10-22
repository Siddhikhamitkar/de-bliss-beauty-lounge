/**
 * The amount after every reduction — the base VAT applies to. Identical
 * arithmetic in both pricing modes: only whether the tax sits inside it or is
 * added on top differs, which the neighbouring row on the receipt states.
 * Works on anything carrying the four money fields — the invoice record and
 * the receipt alike.
 */
export function discountedTotal(bill: {
  subTotal: number
  bundleDiscount: number
  couponDiscount: number
  manualDiscount: number
}): number {
  return (
    Math.round(
      (bill.subTotal - bill.bundleDiscount - bill.couponDiscount - bill.manualDiscount) * 100,
    ) / 100
  )
}
