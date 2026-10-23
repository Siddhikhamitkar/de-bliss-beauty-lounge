# Excel Import — Customers and Inventory

Import customers, retail products and salon-use products from Excel workbooks
(`.xlsx`) in the admin portal. The feature lives on the existing Customers and
Inventory pages, is preview-first, and writes nothing until an explicit
confirmation step.

---

## 1. What it does

- **Customers** — a single workbook flavour with the same fields the customer
  form accepts: first/last name, mobile, email, date of birth, gender, address,
  a first note, and Active.
- **Retail products** — name, brand, SKU, category, cost price, selling price,
  minimum stock, opening stock, Active.
- **Salon-use products** — the same shape but with *Unit of measure* instead of
  the two price columns; salon-use products are never priced.

Every row is validated **server-side** against exactly the rules the create
forms enforce — the import calls the existing `CustomerService` and
`InventoryService`; it introduces no new entities, no new validation and no
schema changes.

## 2. Templates

Each flavour has a downloadable template
(`GET /api/imports/customers/template`, `GET /api/imports/inventory/{kind}/template`
where kind is `retail` or `salon-use`). The workbook holds two sheets:

| Sheet | Content |
| --- | --- |
| data sheet (`Customers`, `Retail Products`, `Salon-Use Products`) | bold, frozen header row only — the import sheet |
| `Example` | guidance only: one filled example row with per-column explanations. **Never imported**, even if filled in; safe to delete |

| Customers | Retail Products | Salon-Use Products |
| --- | --- | --- |
| First name | Product name | Product name |
| Last name | Brand | Brand |
| Mobile | SKU | SKU |
| Email | Category | Category |
| Date of birth | Cost price | Unit of measure |
| Gender | Selling price | Minimum stock |
| Address | Minimum stock | Opening stock |
| Notes | Opening stock | Active |
| Active | Active | |

Accepted date formats: `YYYY-MM-DD` or `DD/MM/YYYY`. Numbers use a dot for
decimals; thousands grouping (`2,000`) is accepted, but a value carrying *both*
separators (`1.234,50` / `1,234.50`) is refused as ambiguous rather than
guessed at. A cell containing a formula (`=B2*2`) refuses its row — plain
values only.

## 3. Safety rules (file level)

Refused before any row is read: anything that is not an `.xlsx` (workbooks are
recognised by their bytes, not the file name), a missing or extra sheet, wrong
or extra columns, an empty file, and files over 5 MB or 1,000 data rows. All
file-level problems arrive as one `400` validation error on the `file` field.

Error reports (`POST …/error-report`, downloadable `.xlsx`) list Row, Field,
Problem and Cell value. Every cell value written into the report that could be
read as a formula (`= + - @` or a tab prefix) is stored with a leading
apostrophe so opening the report cannot execute anything.

## 4. Preview → confirm

`POST …/preview` reads the workbook and returns, without writing anything:
the file name, row counts (valid / invalid / duplicate in file / already
existing), the first rows with a verdict each, and the field-level errors. The
UI refuses to confirm when nothing is valid. **Closing the dialog at any point
before the confirm click leaves the database untouched.**

`POST /api/imports/customers` (with `importDuplicates` opt-in) and
`POST /api/imports/inventory/{kind}` execute the import and return created /
skipped / failed counts plus the opening-stock movement count.

### Duplicate handling

- **Customers** — the existing normalised-mobile rule decides: a row whose
  mobile matches an existing customer is flagged `AlreadyExists` in the preview
  and skipped by default with an explicit "never overwrites" message. The Owner
  or Receptionist may tick *Import duplicates anyway* in the dialog (sent as
  `importDuplicates=true`), which creates the extra records through
  `CustomerService`'s existing opt-in — no merging, no overwriting, ever.
  Rows sharing a mobile inside the same file are flagged `DuplicateInFile` and
  only the first can be imported.
- **Inventory** — a SKU that already exists (case-insensitive) is always
  flagged and skipped. There is no override; the import never rewrites an
  existing product.

### Opening stock

An opening-stock value on a valid row is **not** written into current stock.
After the product is created, the import asks the existing
`InventoryService.ReceiveStock` to record an auditable `OpeningStock` movement
(reference "Excel import", notes naming the source file, performed by the
signed-in user). Existing products never receive stock during an import.

## 5. Permissions

| Surface | Who |
| --- | --- |
| Customers import (template, preview, execute, error report) | Owner and Receptionist (`customers.create`) |
| Inventory import (all four routes) | Owner only (`inventory.manage`) |

Enforced by authorization policies on the controller; direct unauthorized
requests get `403`. The UI hides the inventory import from non-owners, and the
API-surface test pins all eight routes to their declared permissions.

## 6. Atomicity

**The import is atomic per file.** Execution opens one `Serializable`
transaction; every row is re-checked inside it (duplicate mobile / existing
SKU) and written through the domain services. Any failure — a validation
surprise, a database fault, a cancelled request — rolls the whole file back,
and the API reports `succeeded: false` with "The import failed and nothing was
saved". Partial success is never reported as success.

**Retry safety:** re-submitting the same file finds every row already present
(the re-checks see the committed rows) and reports 0 created — a retried
request cannot duplicate customers, products or stock movements.

## 7. Tests

Backend: `DeBliss.UnitTests.Imports` (28 service tests over SQLite —
templates, previews, per-row rules, duplicates, skips, opening-stock
movements, retry idempotence, cancellation leaves nothing, atomic rollback
under an injected fault, formula refusal, error-report injection safety) and
`DeBliss.UnitTests.Api.ImportsApiTests` (11 wire tests — template downloads,
owner end-to-end import and retry, receptionist 403 on all seven inventory
routes, anonymous 401, unknown kind 404). All persistence in tests is mocked
or SQLite; the development database `DeBlissDb-Demo` is never touched.
