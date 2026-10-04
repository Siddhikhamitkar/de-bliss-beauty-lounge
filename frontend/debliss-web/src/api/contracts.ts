/**
 * The JSON that crosses the wire, mirrored field for field from the records in
 * backend/DeBliss.Application/ServiceCatalogue/Dtos. Names are camelCase
 * because that is ASP.NET Core's default serializer policy.
 *
 * These describe what the API sends; the `Save*` types below are the only thing
 * this app ever posts back.
 */

/** How a list is narrowed by record state. Sent by name, as the API binds it. */
export type ServiceStatusFilter = 'All' | 'Active' | 'Inactive'

/** One page of a larger result set, with enough metadata to render a pager. */
export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

/** A row in the service list: enough to recognise and triage a service. */
export interface ServiceSummary {
  id: string
  name: string
  categoryId: string | null
  categoryName: string | null
  durationMinutes: number
  price: number
  isActive: boolean
  isOnlineBookable: boolean
  /**
   * Active, in an active category, with at least one active staff member able
   * to perform it. The API computes this; the UI only reports it.
   */
  isBookable: boolean
  eligibleStaffCount: number
  sortOrder: number
  createdAt: string
  updatedAt: string | null
}

/** A staff member already attached to a service. */
export interface EligibleStaff {
  staffId: string
  firstName: string
  lastName: string
  jobTitle: string | null
  isActive: boolean
}

/** The full record behind one service, including who may perform it. */
export interface Service {
  id: string
  name: string
  description: string | null
  categoryId: string | null
  categoryName: string | null
  isCategoryActive: boolean
  durationMinutes: number
  price: number
  isActive: boolean
  isOnlineBookable: boolean
  isBookable: boolean
  sortOrder: number
  eligibleStaff: EligibleStaff[]
  createdAt: string
  updatedAt: string | null
}

/** A service menu category. */
export interface ServiceCategory {
  id: string
  name: string
  description: string | null
  sortOrder: number
  isActive: boolean
  serviceCount: number
  createdAt: string
  updatedAt: string | null
}

/** A staff member as offered by the eligible-staff selector. */
export interface StaffOption {
  id: string
  firstName: string
  lastName: string
  jobTitle: string | null
  isActive: boolean
  displayName: string
}

/**
 * Payload for creating and editing a service. `eligibleStaffIds` replaces the
 * whole assignment set, which is exactly what the form submits.
 */
export interface SaveServiceRequest {
  name: string | null
  categoryId: string | null
  description: string | null
  durationMinutes: number
  price: number
  isActive: boolean
  isOnlineBookable: boolean
  sortOrder: number
  eligibleStaffIds: string[]
}

/** Payload for creating and editing a category. */
export interface SaveServiceCategoryRequest {
  name: string | null
  description: string | null
  sortOrder: number
  isActive: boolean
}

/** How a staff list is narrowed by record state. Sent by name, as the API binds it. */
export type StaffStatusFilter = 'All' | 'Active' | 'Inactive'

/** A row in the staff list: enough to recognise and triage a team member. */
export interface StaffSummary {
  id: string
  firstName: string
  lastName: string
  jobTitle: string | null
  calendarColor: string | null
  isActive: boolean
  serviceCount: number
  sortOrder: number
  createdAt: string
  updatedAt: string | null
}

/** A service a staff member can perform, as shown on the profile. */
export interface StaffServiceItem {
  serviceId: string
  name: string
  categoryName: string | null
  isActive: boolean
}

/** The full staff profile, including which services the member can perform. */
export interface StaffProfile {
  id: string
  firstName: string
  lastName: string
  email: string | null
  phoneNumber: string | null
  jobTitle: string | null
  calendarColor: string | null
  isActive: boolean
  sortOrder: number
  services: StaffServiceItem[]
  createdAt: string
  updatedAt: string | null
}

/** Payload for creating and editing a staff member. */
export interface SaveStaffRequest {
  firstName: string | null
  lastName: string | null
  email: string | null
  phoneNumber: string | null
  jobTitle: string | null
  calendarColor: string | null
  isActive: boolean
  sortOrder: number
}

/** One working interval in a day, as HH:mm local salon time. */
export interface WorkingInterval {
  start: string
  end: string
}

/** The working pattern for one weekday. A day off carries no intervals. */
export interface DaySchedule {
  dayOfWeek: string
  isWorkingDay: boolean
  intervals: WorkingInterval[]
}

/** The full recurring weekly schedule for one staff member. */
export interface WeekSchedule {
  days: DaySchedule[]
}

/** A single time-off record as it appears on a staff profile. */
export interface StaffTimeOff {
  id: string
  staffId: string
  type: string
  startsAt: string
  endsAt: string
  reason: string | null
  createdAt: string
  updatedAt: string | null
}

/**
 * Payload for creating and editing a time-off record. `type` is the
 * Domain.Staffing.TimeOffType name so the wire format survives enum reorders.
 */
export interface SaveStaffTimeOffRequest {
  type: string | null
  startsAt: string
  endsAt: string
  reason: string | null
}

/** Answer to a single "can this staff member take this slot?" question. */
export interface StaffAvailabilityCheck {
  staffId: string
  start: string
  end: string
  isAvailable: boolean
}

/** How a customer list is narrowed by record state. Sent by name, as the API binds it. */
export type CustomerStatusFilter = 'All' | 'Active' | 'Inactive'

/** A row in the customer list: enough to recognise and triage a customer. */
export interface CustomerSummary {
  id: string
  firstName: string
  lastName: string
  phoneNumber: string | null
  email: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

/**
 * Appointment counts read straight from the scheduling tables. Every count is
 * zero until the appointment module starts writing bookings, at which point
 * the same query simply starts returning real data.
 */
export interface CustomerAppointmentSummary {
  totalAppointments: number
  completedAppointments: number
  cancelledAppointments: number
  noShowAppointments: number
  lastVisitAt: string | null
}

/** The full customer profile as the profile page renders it. */
export interface CustomerProfile {
  id: string
  firstName: string
  lastName: string
  email: string | null
  phoneNumber: string | null
  dateOfBirth: string | null
  gender: string | null
  address: string | null
  isActive: boolean
  appointmentSummary: CustomerAppointmentSummary
  createdAt: string
  updatedAt: string | null
}

/** One note on a customer profile, newest first. */
export interface CustomerNote {
  id: string
  customerId: string
  note: string
  createdAt: string
  updatedAt: string | null
}

/**
 * One past or upcoming appointment on the customer profile. Service names and
 * staff come from booking-time snapshots, so the history never changes when
 * the catalogue or staff list is edited later.
 */
export interface CustomerAppointmentItem {
  id: string
  startsAt: string
  endsAt: string
  status: string
  serviceNames: string[]
  staffNames: string[]
}

/**
 * Payload for creating and editing a customer. `isActive` is omitted (null) by
 * the compact "+ New Customer" form the future appointment screen will post.
 * `allowDuplicatePhone` is the second step of the duplicate warning: the first
 * save without it comes back 409, and the client asks the user before retrying.
 */
export interface SaveCustomerRequest {
  firstName: string | null
  lastName: string | null
  phoneNumber: string | null
  email: string | null
  dateOfBirth: string | null
  gender: string | null
  address: string | null
  isActive: boolean | null
  allowDuplicatePhone: boolean
}

/** Payload for adding to or editing a customer note. Notes are never deleted. */
export interface SaveCustomerNoteRequest {
  note: string | null
}

/**
 * An RFC 9457 problem document. `errors` appears only on a 400 from a save
 * endpoint and is keyed by the request field each message belongs to.
 */
export interface Problem {
  type?: string | null
  title?: string | null
  status?: number | null
  detail?: string | null
  instance?: string | null
  errors?: Record<string, string[]>
}

/**
 * The AppointmentStatus names the API binds, in the order a booking flows
 * through them. Terminal rows never change again, so the calendar renders
 * them as historical and refuses drags.
 */
export const APPOINTMENT_STATUSES = [
  'Pending',
  'Confirmed',
  'CheckedIn',
  'InProgress',
  'Completed',
  'Cancelled',
  'NoShow',
  'Waitlisted',
] as const

/** Statuses whose bookings are finished: the slot is free and the row is immutable. */
export const TERMINAL_STATUSES: readonly string[] = ['Completed', 'Cancelled', 'NoShow']

/** One service line inside a booking request: which service, performed by whom. */
export interface AppointmentServiceLineInput {
  serviceId: string
  staffId: string
}

/**
 * Payload for creating and editing an appointment. Exactly one of customerId
 * (an existing customer) or walkInCustomerName must be set. The end instant is
 * never sent: the server computes it from the snapshot durations.
 */
export interface SaveAppointmentRequest {
  customerId: string | null
  walkInCustomerName: string | null
  walkInCustomerPhone: string | null
  startsAt: string
  services: AppointmentServiceLineInput[]
  notes: string | null
}

/** Partial schedule change from a calendar drag or resize; any subset may be sent. */
export interface UpdateScheduleRequest {
  startsAt?: string
  staffId?: string
  durationMinutes?: number
}

/** Body of the status transition endpoint. Status is the AppointmentStatus name. */
export interface SetAppointmentStatusRequest {
  status: string
  reason: string | null
}

/** One service line on a booked appointment, with its booking-time snapshots. */
export interface AppointmentServiceLine {
  serviceId: string
  serviceName: string
  durationMinutes: number
  unitPrice: number
  staffId: string
  staffFirstName: string
  staffLastName: string
  staffColor: string | null
}

/** One appointment as the calendar and the details panel render it. */
export interface Appointment {
  id: string
  customerId: string | null
  customerName: string
  customerPhone: string | null
  startsAt: string
  endsAt: string
  status: string
  source: string
  notes: string | null
  cancellationReason: string | null
  services: AppointmentServiceLine[]
  totalPrice: number
  totalDurationMinutes: number
  createdAt: string
  updatedAt: string | null
}

/**
 * A dated, non-bookable period on the grid: time off or a blocked period
 * (staff-specific, or salon-wide when staffId is null). Kind is the coarse
 * "TimeOff" | "Blocked" discriminator; Type is the specific enum name.
 */
export interface CalendarMarker {
  id: string
  staffId: string | null
  startsAt: string
  endsAt: string
  kind: string
  type: string | null
  label: string | null
}

/** One staff column of the calendar, including the week pattern the grid shades by. */
export interface CalendarStaff {
  id: string
  firstName: string
  lastName: string
  jobTitle: string | null
  calendarColor: string | null
  sortOrder: number
  workingHours: WeekSchedule
  isActive: boolean
}

/** Everything one calendar range query renders: columns, bookings and non-bookable periods. */
export interface CalendarData {
  from: string
  to: string
  staff: CalendarStaff[]
  appointments: Appointment[]
  markers: CalendarMarker[]
}

/* ------------------------------------------------------------------ */
/* Billing & payments (Phase 1)                                        */
/* ------------------------------------------------------------------ */

/** The only payment methods Phase 1 records; no gateways, no split payments. */
export const PAYMENT_METHODS = ['Cash', 'Card', 'BankTransfer'] as const

export type PaymentMethod = (typeof PAYMENT_METHODS)[number]

export const PAYMENT_METHOD_LABELS: Record<PaymentMethod, string> = {
  Cash: 'Cash',
  Card: 'Card',
  BankTransfer: 'Bank Transfer',
}

export const DISCOUNT_TYPES = ['Fixed', 'Percentage'] as const

export type DiscountType = (typeof DISCOUNT_TYPES)[number]

export const DISCOUNT_TYPE_LABELS: Record<DiscountType, string> = {
  Fixed: 'Fixed',
  Percentage: 'Percentage',
}

export type InvoiceStatusFilter = 'All' | 'Draft' | 'Paid'

export type InvoicePaymentMethodFilter = 'All' | PaymentMethod

/** One row of the billing list. */
export interface InvoiceSummary {
  id: string
  invoiceNumber: string
  customerId: string | null
  customerName: string
  appointmentId: string
  status: string
  currency: string
  subTotal: number
  discountTotal: number
  totalAmount: number
  paymentMethod: string | null
  issuedAt: string | null
  createdAt: string
}

/** One service line of an invoice, with the price snapshot actually charged. */
export interface InvoiceItem {
  id: string
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
  serviceId: string | null
  appointmentServiceId: string | null
  sortOrder: number
}

export interface InvoicePayment {
  id: string
  amount: number
  currency: string
  method: string
  status: string
  reference: string | null
  paidAt: string
}

/** A full invoice, including its snapshot line items and recorded payments. */
export interface Invoice {
  id: string
  invoiceNumber: string
  status: string
  currency: string
  appointmentId: string
  customerId: string | null
  customerName: string
  walkInCustomerName: string | null
  couponId: string | null
  couponCode: string | null
  subTotal: number
  couponDiscount: number
  manualDiscount: number
  discountTotal: number
  totalAmount: number
  issuedAt: string | null
  createdAt: string
  updatedAt: string | null
  items: InvoiceItem[]
  payments: InvoicePayment[]
}

/** The checkout payload; every field is validated again on the server. */
export interface CheckoutInvoiceRequest {
  couponCode: string | null
  manualDiscountType: DiscountType | null
  manualDiscountValue: number | null
  paymentMethod: PaymentMethod | null
  amount: number | null
  reference: string | null
}

/** One printable line of a receipt. */
export interface ReceiptLine {
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

/** The printable receipt for a paid invoice. */
export interface Receipt {
  salonName: string
  invoiceId: string
  invoiceNumber: string
  customerName: string
  items: ReceiptLine[]
  subTotal: number
  couponCode: string | null
  couponDiscount: number
  manualDiscount: number
  totalAmount: number
  currency: string
  paymentMethod: string
  paymentReference: string | null
  paidAt: string
  issuedAt: string
}

/** What the checkout screen sends to preview a coupon against a subtotal. */
export interface ValidateCouponRequest {
  code: string
  subtotal: number
}

/** The validated coupon preview: exactly what the checkout screen displays. */
export interface CouponValidation {
  code: string
  discountType: DiscountType
  discountValue: number
  discountAmount: number
}

export type CouponStatusFilter = 'All' | 'Active' | 'Inactive'

/** One coupon as the management screen lists it. */
export interface Coupon {
  id: string
  code: string
  discountType: DiscountType
  discountValue: number
  isActive: boolean
  validFrom: string | null
  validUntil: string | null
  createdAt: string
  updatedAt: string | null
}

/** The coupon create/edit form; isActive null leaves the flag untouched on edit. */
export interface SaveCouponRequest {
  code: string
  discountType: DiscountType
  discountValue: number
  validFrom: string | null
  validUntil: string | null
  isActive: boolean | null
}

/* ------------------------------------------------------------------ */
/* Dashboard (receptionist)                                            */
/* ------------------------------------------------------------------ */

/**
 * The receptionist's read-only snapshot of today — appointment workload
 * counts and operational lists only. Deliberately free of money: prices,
 * revenue and every other owner-level figure stay in the billing module.
 */
export interface ReceptionistDashboard {
  generatedAt: string
  today: DashboardToday
  schedule: DashboardAppointment[]
  upcoming: DashboardAppointment[]
  staff: DashboardStaffMember[]
  alerts: DashboardAlerts
  popularServices: DashboardPopularService[]
}

/** Appointment workload counters for the current day — operational only. */
export interface DashboardToday {
  date: string
  total: number
  completed: number
  inProgress: number
  upcoming: number
  noShow: number
  cancelled: number
  walkIns: number
}

/** One booking in today's schedule or the upcoming list, flattened for a row. */
export interface DashboardAppointment {
  id: string
  customerId: string | null
  customerName: string
  startsAt: string
  endsAt: string
  status: string
  servicesLabel: string
  staffName: string | null
  staffColor: string | null
  canCheckout: boolean
}

/** A staff member's derived right-now status, computable from existing data only. */
export interface DashboardStaffMember {
  id: string
  firstName: string
  lastName: string
  jobTitle: string | null
  color: string | null
  status: string
  statusNote: string | null
}

/** Front-desk alerts: birthdays, first visits and the freshest reception notes. */
export interface DashboardAlerts {
  birthdaysToday: number
  birthdayNamesToday: string[]
  birthdayNamesLaterThisWeek: string[]
  firstVisitsToday: number
  recentNotes: DashboardNote[]
}

/** A customer reception note surfaced for the front desk. */
export interface DashboardNote {
  customerId: string
  customerName: string
  note: string
  createdAt: string
}

/** An operational workload indicator: how often a service is booked today. */
export interface DashboardPopularService {
  name: string
  count: number
}
