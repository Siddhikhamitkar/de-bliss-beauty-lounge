import { request, toQueryString } from './client.ts'
import type {
  AddInvoiceLineRequest,
  CheckoutInvoiceRequest,
  Coupon,
  CouponStatusFilter,
  CouponValidation,
  Invoice,
  InvoicePaymentMethodFilter,
  InvoiceStatusFilter,
  InvoiceSummary,
  PagedResult,
  Receipt,
  SaveCouponRequest,
  SetTipRequest,
  UpdateInvoiceLineRequest,
  ValidateCouponRequest,
} from './contracts.ts'

/** Everything the invoice list can be narrowed by. Omitted keys are not sent. */
export interface InvoiceSearch {
  search?: string
  status?: InvoiceStatusFilter
  paymentMethod?: InvoicePaymentMethodFilter
  customerId?: string
  page?: number
  pageSize?: number
}

export function searchInvoices(search: InvoiceSearch): Promise<PagedResult<InvoiceSummary>> {
  return request(
    'GET',
    `/invoices${toQueryString({
      search: search.search,
      status: search.status,
      paymentMethod: search.paymentMethod,
      customerId: search.customerId,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}

export function getInvoice(id: string): Promise<Invoice> {
  return request('GET', `/invoices/${id}`)
}

/** Starts a draft invoice from a completed appointment; a repeat call resumes it. */
export function startInvoice(appointmentId: string): Promise<Invoice> {
  return request('POST', '/invoices', { appointmentId })
}

/** Starts a standalone retail draft for a walk-in, untied to any booking. */
export function createRetailInvoice(walkInCustomerName: string): Promise<Invoice> {
  return request('POST', '/invoices', { walkInCustomerName })
}

/** Adds a retail product to a draft bill; the server prices it from the catalogue. */
export function addInvoiceLine(id: string, body: AddInvoiceLineRequest): Promise<Invoice> {
  return request('POST', `/invoices/${id}/lines`, body)
}

/** Changes how many of a product the customer is buying; product lines only. */
export function updateInvoiceLine(
  id: string,
  lineId: string,
  body: UpdateInvoiceLineRequest,
): Promise<Invoice> {
  return request('PUT', `/invoices/${id}/lines/${lineId}`, body)
}

/** Removes a product line from a draft bill; service lines come from the booking and stay. */
export function removeInvoiceLine(id: string, lineId: string): Promise<Invoice> {
  return request('DELETE', `/invoices/${id}/lines/${lineId}`)
}

/** Records the payment and closes the invoice; returns the receipt to print. */
export function checkoutInvoice(id: string, body: CheckoutInvoiceRequest): Promise<Receipt> {
  return request('POST', `/invoices/${id}/checkout`, body)
}

/**
 * Saves the tip choice on a draft so an interrupted checkout resumes with it.
 * The server persists the choice only — the charged amount is recomputed at
 * checkout against the then-current totals.
 */
export function setInvoiceTip(id: string, body: SetTipRequest): Promise<Invoice> {
  return request('PUT', `/invoices/${id}/tip`, body)
}

/** Reprints the receipt for a paid invoice. */
export function getReceipt(id: string): Promise<Receipt> {
  return request('GET', `/invoices/${id}/receipt`)
}

/** Previews a coupon against a subtotal; the server is the source of truth. */
export function validateCoupon(body: ValidateCouponRequest): Promise<CouponValidation> {
  return request('POST', '/coupons/validate', body)
}

/** Everything the coupon list can be narrowed by. Omitted keys are not sent. */
export interface CouponSearch {
  search?: string
  status?: CouponStatusFilter
  page?: number
  pageSize?: number
}

export function searchCoupons(search: CouponSearch): Promise<PagedResult<Coupon>> {
  return request(
    'GET',
    `/coupons${toQueryString({
      search: search.search,
      status: search.status,
      page: search.page,
      pageSize: search.pageSize,
    })}`,
  )
}

export function getCoupon(id: string): Promise<Coupon> {
  return request('GET', `/coupons/${id}`)
}

export function createCoupon(body: SaveCouponRequest): Promise<Coupon> {
  return request('POST', '/coupons', body)
}

export function updateCoupon(id: string, body: SaveCouponRequest): Promise<Coupon> {
  return request('PUT', `/coupons/${id}`, body)
}

export function setCouponStatus(id: string, isActive: boolean): Promise<Coupon> {
  return request('PATCH', `/coupons/${id}/status`, { isActive })
}
