# Billing & Payments module — specification

This document is the authoritative description of the Billing & Payments
module (Phase 1): the checkout workflow the receptionist uses to turn a
completed appointment into a paid invoice, the coupon catalogue behind it, and
the billing workspace used to search and reprint. Where this document and the
code disagree, this document is corrected or the code is changed — the two are
never allowed to drift apart silently.

Phase 1 is deliberately **simple**: no split payments, no gateways, no refunds,
no accounting engine. It exists so the salon can take money at the desk.

---

## Scope

In scope for this module:

- The checkout workflow: a completed appointment becomes an invoice whose lines
  are snapshots of the booked services — nothing is retyped.
- Coupons: a catalogue with create/edit/deactivate, and validation used both to
  preview a discount and to apply it at checkout.
- Manual discounts: a fixed amount or a percentage, entered by the
  receptionist.
- Payment collection: Cash, Card or Bank Transfer (with an optional
  reference), recorded against the invoice.
- The receipt: printable after payment and reprintable later.
- The billing workspace: search invoices by number/customer, filter by status
  and payment method, open the invoice's details.

Deliberately **not** built in Phase 1 (any of these returning would be scope
creep):

- Split payments, payment gateways, refund workflows.
- Package redemption and gift cards.
- Advanced accounting and complex promotions.

The [Loyalty & Rewards extension](LOYALTY-REWARDS-SPECIFICATION.md) adds rewards
discounts to the existing VAT/tip checkout. Operational APIs use the existing
Owner/Receptionist policy; loyalty settings use OwnerOnly. Loyalty is a discount,
not a tender: `DiscountTotal` includes `LoyaltyDiscount`; Cash/Card/BankTransfer
records only the final payable amount. Invoice/receipt snapshots include earned
and redeemed points. Checkout preview and finalization share backend calculations,
and serializable finalization commits invoice, payment, stock and points together.

---

## Concepts

### The invoice and its snapshot lines

An invoice is started from a **completed appointment** (`POST /api/invoices`
with `appointmentId`). The appointment's service lines are copied onto the
invoice at their **booking-time prices** — the same snapshot values the
appointment already carries. A later edit to the service catalogue can never
rewrite what was invoiced.

- Repeat calls resume the existing invoice instead of raising a second bill for
  the same visit (`WasCreated: false`).
- Only a `Completed` appointment can be checked out (`409` otherwise); a
  missing appointment is a `404`.
- `SubTotal` is the sum of the line totals; `TotalAmount` starts equal to it.
- Invoice numbers are `INV-{yyyy}-{sequence:D4}` per year, allocated from the
  highest existing sequence that year.

Invoice status: `Draft` → `Paid`. A `Draft` invoice is awaiting checkout and may
be resumed from the billing list. Checkout on an already-`Paid` invoice is a
`409` — the historical record is never reopened. Invoices and payments are
**never physically deleted**; a coupon is retired by deactivating it.

### Single currency

Phase 1 trades in **AED only** (`BillingLimits.CurrencyCode`), fixed on the
invoice at creation. All monetary values are `decimal` end to end — database
columns, DTOs, domain and UI — never floating point.

### Coupons

`Coupon` is the one new entity: `Code` (unique, stored **trimmed and
upper-cased**, max 50), `DiscountType` (`Fixed` | `Percentage`),
`DiscountValue`, `IsActive`, and an inclusive `ValidFrom`/`ValidUntil` window
(either side nullable). Matching is case-insensitive because of the stored
form — the receiving side trims and upper-cases before lookup, so a code works
however it was typed or pasted.

A coupon is applicable only when it is **active and today falls inside the
validity window** (`CouponRules`). Unknown, inactive and out-of-window codes
all produce exactly one message — `Coupon code is invalid or expired.` — so the
customer never learns which codes exist.

The **backend is the only authority**: the checkout re-validates the coupon
inside the same request that records the payment; the frontend preview from
`POST /api/coupons/validate` is a quotation, not a grant. Both paths share
`CouponRules` and `DiscountMath`, so the quoted discount and the charged
discount can never disagree.

One coupon per invoice. The applied coupon's id, code and computed discount are
stored on the invoice, so a paid bill stays explainable even if the coupon is
later edited or deactivated.

### Discounts

A percentage discount rounds to fils (two decimals, away from zero) via
`DiscountMath.Compute` — the single place discount amounts are computed.
Manual discounts accept a **fixed amount** or a **percentage** (0–100
exclusive of 0, i.e. > 0 and ≤ 100); fixed values must be > 0.

Coupon discount and manual discount are stored **separately**
(`CouponDiscount`, `ManualDiscount`) and never merged into one anonymous value.
`DiscountTotal` is their sum, kept for list display.

The rules, enforced at checkout:

- Each discount on its own can never exceed the subtotal.
- The combined reduction can never exceed the subtotal
  (`Discounts cannot exceed the subtotal.`).
- The final total can never go below zero — with the caps above this is a
  mathematical guarantee, not a clamp.

### Payment

Phase 1 payment methods are **Cash, Card and Bank Transfer** — nothing else
parses. The reference field is optional (max 100 chars, trimmed; shown as
"Reference", "Transaction reference" or "Receipt reference" depending on the
method in the UI).

For Phase 1 the **payment amount must equal the invoice total exactly**; a
mismatch is a field error naming the expected figure. One completed payment
per invoice — split payments would arrive later by adding more `Payment` rows
to the same collection.

Checkout is **atomic**: the coupon snapshot, both discounts, the total, the
payment row and the `Paid` status are written in one save, so an invoice is
never left half checked out. `IssuedAt` and the payment's `PaidAt` are stamped
from the server clock (`TimeProvider`) in the same transaction.

### The receipt

`ReceiptDto` is composed from the stored invoice — salon name
("De Bliss Beauty Lounge"), invoice number, customer (or walk-in name),
service lines with snapshot prices, subtotal, coupon code + discount, manual
discount, total, currency, payment method, reference and payment date/time.
The receipt for an unpaid invoice is a `409`; a paid invoice's receipt can be
reprinted at any time via `GET /api/invoices/{id}/receipt`.

---

## API surface

All routes live under `/api`; controllers are thin — they bind DTOs, call the
application service, and map failures to RFC 7807 problems. EF entities are
never exposed.

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/invoices` | Paged list; `search` (invoice number or customer), `status`, `paymentMethod`, `customerId`, `page`, `pageSize` |
| GET | `/api/invoices/{id}` | Invoice detail with items and payments |
| POST | `/api/invoices` | Start (or resume) a draft from `{ appointmentId }` |
| POST | `/api/invoices/{id}/checkout` | Validate discounts, record the payment, mark Paid; returns the receipt |
| GET | `/api/invoices/{id}/receipt` | Reprint the receipt of a paid invoice |
| POST | `/api/coupons/validate` | Preview a coupon against a subtotal |
| GET | `/api/coupons` | Paged list; `search`, `status`, paging |
| POST | `/api/coupons` | Create |
| GET | `/api/coupons/{id}` | Detail |
| PUT | `/api/coupons/{id}` | Update |
| PATCH | `/api/coupons/{id}/status` | Activate / deactivate |
| POST | `/api/payments` | Reserved for later payment extensions |

Checkout field-error keys (the frontend attaches them to the matching input):
`couponCode`, `manualDiscountType`, `manualDiscountValue`, `amount`,
`paymentMethod`, `reference`. The coupon preview endpoint reports under `code`.

Failure semantics: `400` validation with per-field messages; `404` unknown
invoice/coupon/appointment; `409` for state conflicts (appointment not
completed, invoice already paid or not awaiting checkout, receipt of an unpaid
invoice, duplicate coupon code).

---

## Frontend

- **Billing workspace** (`BillingPage`): the invoices list with filters
  (search box, status select, payment-method select, Apply), paging, a status
  pill per row, and an `InvoiceDrawer` with full detail — items, discounts,
  payment block, and actions: *Resume checkout* for drafts, *Print receipt* for
  paid invoices. A `revision` prop reloads the list after a payment lands.
- **Checkout** (`CheckoutPage`): a full-screen workspace opened from the
  calendar's completed-appointment panel or from a draft invoice. It shows the
  invoice lines and totals read-only (nothing is retyped), applies a coupon by
  code (with a preview line and ✕ Remove), adds a manual discount (type +
  value), picks the payment method, shows the reference field labelled per
  method, and confirms with the amount defaulted to the exact total. On success
  it hands over to the **receipt overlay** (*Print* / *Done*). Resume of a paid
  invoice loads the printable receipt instead.
- **Coupons** (`CouponsPage`): list with search + status filter, active status
  toggling, and a drawer-based form (code, type, value, validity window) used
  for both create and edit. Server field errors are shown on the matching
  inputs; duplicate codes surface as the conflict message.
- **Customer drawer → Sales tab** (`CustomerSales`): the customer's invoices
  read from the invoicing module — the tab stops saying "No sales yet." once
  billing exists; it reflects the invoice rows (number, status, total, issued
  date) for that customer.

---

## Invariants

1. Money is `decimal` everywhere; the currency is AED in Phase 1; prices are
   snapshot values, never live lookups.
2. The backend validates the coupon at checkout; the frontend is never trusted
   for a discount.
3. Coupon and manual discount are stored separately; combined discounts can
   never exceed the subtotal; the total can never go below zero.
4. The payment amount equals the total; one completed payment per invoice in
   Phase 1.
5. Checkout is one atomic save; a resume returns the existing invoice for the
   appointment.
6. Invoices and payments are never deleted; coupons are only deactivated.
7. Controllers stay thin; business rules live in the Application layer;
   entities are never exposed.

---

## Tests

Backend (`tests/DeBliss.UnitTests`):

- `Billing/InvoiceServiceTests` — start/resume, snapshot lines, coupon
  application (case-insensitive, inactive, expired, over-subtotal, combined
  cap), manual discounts (fixed/percentage, invalid values), amount equality,
  payment method restriction, reference limits, atomic checkout, receipt
  composition, conflict states.
- `Billing/CouponServiceTests` — CRUD, code normalization/dedup, validity
  window rules, validation messages (exact `Coupon code is invalid or
  expired.`).
- `Api/BillingApiTests` — the HTTP surface end to end: status codes, JSON
  shapes, field-error keys, `409`s, permissions constants presence.
- Model tests cover the new `Coupon` entity and the invoice discount columns.

Frontend (`frontend/debliss-web/src`, vitest + Testing Library):

- `billing/CheckoutPage.test.tsx` — load without re-entry, coupon apply /
  remove / invalid (exact message) / fixed + percentage discounts, method
  specific reference labels, full payment body, receipt overlay, field errors,
  already-paid resume, retry after load failure.
- `billing/BillingPage.test.tsx` — loading, list contents, filters wiring,
  empty state, failure retry, drawer actions (resume checkout, print receipt),
  coupons hand-off.
- `coupons/CouponsPage.test.tsx` — list, status filter, deactivate, error
  banner, create/edit with normalization and backend field errors.
- `customers/CustomerDrawer.test.tsx` — the Sales tab lists the customer's
  invoices (and shows the empty state when there are none).

---

## Database

One migration: **`20261004111115_AddCouponsAndInvoiceDiscounts`** — creates the
`Coupons` table (unique index on `Code`, plus an index on `IsActive`) and adds
the discount columns (`CouponId` FK with `Restrict` delete behaviour,
`CouponCode`, `CouponDiscount`, `ManualDiscount`) to `Invoices`, all money as
`decimal(18,2)`. `InitialCreate` is never modified; existing data is preserved;
the database is never reset.
