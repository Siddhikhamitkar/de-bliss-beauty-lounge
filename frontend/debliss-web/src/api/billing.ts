import { request, toQueryString } from './client.ts'
import type {
  CheckoutInvoiceRequest,
  Coupon,
  CouponStatusFilter,
  Invoice,
  InvoicePaymentMethodFilter,
  InvoiceStatusFilter,
  InvoiceSummary,
  PagedResult,
  Receipt,
  SaveCouponRequest,
  ValidateCouponRequest,
  CouponValidation,
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

/** Records the payment and closes the invoice; returns the receipt to print. */
export function checkoutInvoice(id: string, body: CheckoutInvoiceRequest): Promise<Receipt> {
  return request('POST', `/invoices/${id}/checkout`, body)
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
