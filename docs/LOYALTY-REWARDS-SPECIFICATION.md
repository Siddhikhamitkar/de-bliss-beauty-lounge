# Loyalty & Rewards

## Scope and access

Internal rewards for existing customer records, integrated with invoice checkout.
The program defaults to **disabled**. Existing paid invoices are not backfilled.
Disabling the program stops new earning and redemption without changing history.

- Owner alone: `GET` and `PUT /api/loyalty/settings`, and the `/loyalty` page.
- Owner and Receptionist: customer loyalty summary/history, checkout preview,
  and checkout redemption through existing billing routes.
- Staff, Customer, and anonymous users cannot access these internal APIs.
- No balance editor, manual adjustment, customer portal, gateway, or independent
  payment/redemption endpoint is provided.

## Settings

| Setting | Default | Validation |
| --- | --- | --- |
| Enabled | false | Required boolean |
| Earning percentage | 5 | 0–100, at most 2 decimals |
| AED per point | 1 | 0.0001–1000, at most 4 decimals |
| Minimum points to redeem | 0 | 0–1,000,000,000, at most 2 decimals |
| Maximum invoice redemption percentage | 50 | 0–100, at most 2 decimals |
| Allow partial redemption | true | Required boolean |
| Minimum eligible invoice amount | none | Null or 0–1,000,000,000, at most 2 decimals |

Defaults are read-only until the Owner saves configuration. No business data is
seeded. Settings changes do not revalue old invoice amounts or rewrite ledger
entries. Current conversion value determines the AED value of unspent points.

**No partial redemption** means the customer's entire available balance must be
redeemed. If that balance cannot fit the invoice cap, redemption is unavailable;
the system does not silently redeem only the capped portion.

## Calculation and rounding

All authoritative calculations use server-side `decimal` values. Points and AED
amounts have two decimals; conversion values have four decimals.

1. Begin with stored invoice subtotal less bundle, coupon, and manual discounts.
2. Determine eligible spend before loyalty: exclude included VAT if prices are
   inclusive; never include tips. Minimum-invoice and percentage caps use this
   pre-loyalty, VAT-exclusive amount.
3. Maximum discount is that eligible amount multiplied by the configured cap,
   bounded by the amount before loyalty. Maximum points are bounded by balance
   and discount/conversion, rounded **down** to two decimals.
4. Requested points must meet the minimum, available balance, cap and partial
   redemption rule. Zero means no redemption. Negative values, excess precision,
   and redemptions worth less than AED 0.01 are rejected.
5. Loyalty discount is points × current AED-per-point, rounded **down** to fils
   so rounding never exceeds a configured cap. Subtract it from the merchandise
   amount before recomputing VAT and percentage tips, following billing's existing
   discount treatment. Inclusive-price discounts reduce the displayed price;
   their VAT component is recomputed rather than treating points as cash tender.
6. Eligible spend for earning is the resulting merchandise amount excluding VAT.
   Earned points = eligible spend × earning percentage / 100, rounded to two
   decimals **away from zero**. No earning on tips or redeemed rewards.
7. Final payable = discounted merchandise + exclusive VAT (if applicable) + tip.
   The existing Cash/Card/BankTransfer payment must match exactly. A fully
   discounted zero-payable invoice is allowed and earns zero points.

Example, exclusive VAT: AED 200 merchandise, AED 20 loyalty discount, 5% VAT,
AED 10 fixed tip, and 5% earning produces AED 180 eligible spend, 9 points,
AED 9 VAT, and AED 199 collected. Points are a discount, not an additional
payment row or a new payment method.

## Persistence and concurrency

- `LoyaltySettings`: singleton configuration, audit timestamps and updating user.
- `CustomerLoyaltyTransactions`: append-only signed point movements linked to
  existing Customer and Invoice rows, timestamp, source user, reason, eligible
  spend, earning percentage and conversion snapshots, and idempotency key.
- Types: Earn, Redeem, Reversal. Reversals can reference their original transaction.
- No balance projection: ledger sums are authoritative. Lifetime earned and gross
  redeemed are derived from their corresponding original ledger entries.
- Restrictive foreign keys preserve financial history. Unique idempotency keys
  and a filtered unique invoice/type index prevent duplicate Earn/Redeem entries.
- EF rejects edits/deletes of ledger rows; corrections require additional entries.
- Invoice snapshots: `LoyaltyDiscount`, `LoyaltyPointsRedeemed`,
  `LoyaltyPointsEarned`. `DiscountTotal` includes loyalty exactly once.
- Checkout begins a serializable database transaction **before** invoice and
  balance reads. Range locks protect against spending the same ledger balance in
  simultaneous checkouts. Payment, invoice, stock and loyalty movements commit
  together. Failed validation or persistence commits none of them.
- Invoice status and line revision are concurrency tokens. Every line mutation
  increments the parent revision, including free products whose totals do not
  change, so a stale line edit cannot alter a paid invoice.
- A paid invoice retry returns the existing
  billing conflict (409), never a second payment or award. SQL Server deadlock
  victims return a retryable 409 after rollback; callers reload before retrying.
- Preview is advisory and writes nothing. Checkout revalidates live settings,
  stock, invoice and points independently of browser figures.

## APIs and UI

| Endpoint | Purpose |
| --- | --- |
| `GET /api/loyalty/settings` | Owner configuration |
| `PUT /api/loyalty/settings` | Validate and save Owner configuration |
| `GET /api/customers/{id}/loyalty` | Available points, lifetime earned, redeemed, debt, recent entries |
| `GET /api/customers/{id}/loyalty/transactions?page=1&pageSize=50` | Paginated immutable history |
| `POST /api/invoices/{id}/checkout-preview` | Server-calculated invoice and redemption quote; no writes |
| `POST /api/invoices/{id}/checkout` | Existing checkout plus optional `loyaltyPointsToRedeem` (default 0) |

The preview accepts the existing checkout request shape; tender method, reference,
and collected amount are only validated during finalization. It returns server
invoice totals and operational loyalty rules/limits, not the Owner settings API.

The Owner Loyalty page uses existing navigation and route authorization. Customer
details include a read-only Loyalty tab. Checkout displays balance, AED value,
eligibility, caps and discount breakdown; finalized receipts and invoice details
use stored loyalty snapshots. Customerless walk-ins cannot earn or redeem.

## Refund limitation and future correction policy

Billing currently has refund-related enums and payment references but **no refund
or void workflow**. This module does not invent one and cannot automatically
reverse points for out-of-band database edits. Draft/unpaid/void invoices do not
award points through checkout.

Before introducing refunds, their transaction must append proportional earning
reversals and redemption restorations, linked to original ledger entries and a
unique refund-operation idempotency key. Calculate cumulatively from original
snapshots and subtract earlier reversals to avoid repeated rounding or duplicate
restoration. Do not delete history. The Owner must confirm eligible refund
allocation (including tax/tips), proportional versus full reward restoration,
and debt recovery before that workflow ships. If earned points were already
spent, represent the shortfall as auditable points debt; available points remain
zero until later earnings cover it. Summary supports debt presentation, but no
current API creates refund/debt entries or manual adjustments.

## Database and verification rules

Persistent migrations and live verification target **DeBlissDb-Demo only**. No
reset, scratch SQL database, fake customer or fake sale is permitted. The user
explicitly approved existing ephemeral in-memory SQLite fixtures for automated
regression and integration tests. Those tests must never connect to Demo.

Concurrency integration tests use independent SQLite connections; SQL Server
serializable/range-lock configuration is verified by implementation and migration
inspection, not by claiming a synthetic live financial transaction occurred.
Live browser verification must preserve business records and leave the program
disabled until the Owner intentionally enables it.
