# Customer mobile-number accounts

Customers are identified by their **mobile number**. Email is optional
everywhere. A customer record exists as soon as the front desk books them; the
customer turns it into a sign-in account by proving they own the number with a
one-time code (OTP) and choosing a password.

Both customer clients (`DeBlissCustomerExperience`: Next.js web and Expo
mobile) use only the endpoints below. There is one backend and one database.

## Identity rules

- **Canonical number.** `Customers.NormalizedPhoneNumber` holds the number in
  E.164 (`+971501234567`), derived on every save from `PhoneNumber` by
  `MobileNumber.Normalize` (Domain). UAE local forms are understood:
  `050 123 4567`, `501234567`, `00971…`, `+971 050…`.
- **One number, one customer.** Unique filtered index
  `IX_Customers_NormalizedPhoneNumber`. The front desk can no longer save a
  second customer with the same number (`allowDuplicatePhone` is ignored → 409),
  and the Excel import skips numbers already on file or repeated in the file.
- **Account status.** `Customers.AccountStatus`: `PendingActivation` (default for
  every front-desk, booking and import record) or `Active`.
  `Customers.MobileVerifiedAt` is set when a code is proven and cleared whenever
  the number changes without proof.
- **Mobile sign-in** requires an `Active` account **and** a verified number.

## Receptionist bookings

`POST/PUT /api/appointments` accepts `newCustomer { firstName, lastName,
phoneNumber, email? }` instead of `customerId`. The number is matched: an
existing customer is reused untouched, otherwise a new one is created
(pending activation, no email needed). A walk-in who leaves a readable number
is treated the same way; a walk-in without a number stays anonymous. The
booking is validated first, so a refused booking never creates a customer.
Concurrent bookings for the same new number are resolved by the unique index
(`ICustomerRepository.AddOrGetByPhoneAsync`): both land on one record.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/api/public/customer-auth/otp` | anonymous | `{ phoneNumber, purpose: "activation" \| "password-reset" }` → always `202` |
| POST | `/api/public/customer-auth/otp/verify` | anonymous | `{ phoneNumber, purpose, code }` → `{ verificationToken, expiresAt, needsProfile }` |
| POST | `/api/public/customer-auth/activate` | anonymous | `{ verificationToken, password, firstName?, lastName?, email? }` → `AuthUserDto` + session cookie; `409` if already active |
| POST | `/api/public/customer-auth/password/reset` | anonymous | `{ verificationToken, password }` → `204`; ends older sessions |
| POST | `/api/auth/login` | anonymous | exactly one of `email` / `phoneNumber`, plus `password` |
| GET/PUT | `/api/customer/me` | customer | profile; PUT `{ firstName, lastName, email? }` (blank email removes it) |
| POST | `/api/customer/me/phone/otp` | customer | `{ phoneNumber }` → code to the **new** number, `202` |
| POST | `/api/customer/me/phone/verify` | customer | `{ phoneNumber, code }` → switches the number |
| POST | `/api/public/customers/register` | anonymous | **retired**: `410 Gone` (it allowed claiming a number without proof) |

`needsProfile` is true when no customer record holds the number (a brand-new
sign-up): activation then requires first and last name.

## Security properties

- **No enumeration.** A code request answers the same `202` (and applies the
  same cooldown) whether or not the number has an account; reset codes are only
  actually sent to real accounts. Login failures are one generic `401`, with the
  same hashing work done for unknown accounts.
- **Codes.** 6 digits, salted SHA-256 at rest, 10-minute expiry, 5 wrong guesses
  per code, a newer code kills older ones, single purpose.
- **Verification token.** 256-bit random, SHA-256 at rest, 15 minutes, single use.
- **Rate limits.** Per number and purpose: 60 s resend cooldown, 5 codes/hour
  (`429` + `Retry-After`). Per client IP: 10 requests/min on the code
  endpoints, 20/min on login.
- **Passwords.** ASP.NET Core Identity PBKDF2 (unchanged).
- **Sessions after reset.** `Users.SecurityStamp` is rotated by a reset and
  carried in the JWT (`sstamp`); customer tokens with a stale stamp are refused.
  Team (owner/receptionist/staff) tokens are not affected by this check.

## Existing customers

Customers who registered with an email keep signing in with it — nothing is
reset. Their mobile number is not verified yet, so mobile sign-in opens after
they prove it once: **Forgot password** by mobile (sets a new password and
verifies the number). Front-desk customers without a login use **Activate your
account**.

## Configuration

`appsettings.json` → `CustomerOtp`:

| Setting | Default | Notes |
|---|---|---|
| `Provider` | *(empty → Disabled)* | `TwilioSms` in production; `Log` in Development (writes codes to the API console — never use outside development) |
| `Sms:MessagingServiceSid` or `Sms:From` | — | Twilio sender for SMS (a number must be SMS-capable for the UAE) |
| `CodeLength`, `CodeLifetime`, `MaxAttempts`, `ResendCooldown`, `MaxSendsPerHour`, `VerificationTokenLifetime` | 6, 00:10:00, 5, 00:01:00, 5, 00:15:00 | optional overrides |

Twilio credentials are shared with WhatsApp reminders: `Twilio:AccountSid`,
`Twilio:AuthToken` — supply them via environment variables
(`Twilio__AccountSid`, `CustomerOtp__Provider`, `CustomerOtp__Sms__From`, …),
never in files. With no provider configured nothing is sent and an error is
logged (without the code), so customers cannot activate until it is set.

Behind a reverse proxy, enable forwarded headers so per-IP rate limits see the
real client address.

## Migration

`20261116100720_AddCustomerMobileAuthentication`: additive only. Adds
`Customers.NormalizedPhoneNumber / AccountStatus / MobileVerifiedAt`,
`Users.SecurityStamp`, table `CustomerVerificationCodes`; makes `Users.Email`
nullable (its unique index becomes filtered). Backfills the normalized number
(if two existing records share a number, only one — the one with a login, else
the oldest — gets it; the others stay NULL for the front desk to merge) and
marks customers that already have a login `Active`. Nothing is deleted.
