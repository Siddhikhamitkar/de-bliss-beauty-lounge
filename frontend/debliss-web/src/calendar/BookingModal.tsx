import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import type {
  AppointmentServiceLineInput,
  Bundle,
  CustomerSummary,
  EligibleStaff,
  SaveAppointmentRequest,
  SaveCustomerRequest,
  ServiceSummary,
} from '../api/contracts.ts'
import { getService, listBookableBundles, searchIndividualServices } from '../api/catalogue.ts'
import { createCustomer, searchCustomers } from '../api/customers.ts'
import { checkStaffAvailability } from '../api/staff.ts'
import { createAppointment, updateAppointment } from '../api/scheduling.ts'
import { ConfirmationDialog } from '../components/ConfirmationDialog.tsx'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { formatDuration, formatPrice } from '../format.ts'
import { dayKeyOf, formatClock, parseDayKey } from './calendarTime.ts'
import type { BookingState } from './CalendarPage.tsx'

interface BookingModalProps {
  booking: Exclude<BookingState, null>
  onClose: () => void
  onSaved: () => void
}

type CustomerMode = 'existing' | 'new' | 'walkin'

interface DraftLine {
  serviceId: string
  serviceName: string
  durationMinutes: number
  unitPrice: number
  staffId: string | null
  eligible: EligibleStaff[]
}

interface AvailabilityWindow {
  staffId: string
  start: Date
  durationMinutes: number
}

const CUSTOMER_MODES: ReadonlyArray<{ value: CustomerMode; label: string }> = [
  { value: 'existing', label: 'Existing customer' },
  { value: 'new', label: 'New customer' },
  { value: 'walkin', label: 'Walk-in' },
]

function defaultTimeValue(startMinutes: number | null): string {
  return formatClock(startMinutes ?? 9 * 60)
}

function formatClockOf(date: Date): string {
  return formatClock(date.getHours() * 60 + date.getMinutes())
}

function composeDateTime(dateKey: string, timeValue: string): Date | null {
  if (dateKey === '' || timeValue === '') {
    return null
  }
  const [hours, minutes] = timeValue.split(':').map(Number)
  const day = parseDayKey(dateKey)
  day.setHours(hours, minutes, 0, 0)
  return day
}

class ClientValidationError extends Error {}

/**
 * The whole booking decision in one dialog: who, what, who performs it, when.
 * The backend stays the authority — availability conflicts and validation come
 * back as problem documents and are shown next to the fields they belong to.
 */
export function BookingModal({ booking, onClose, onSaved }: BookingModalProps) {
  const isEdit = booking.kind === 'edit'
  const appointment = booking.kind === 'edit' ? booking.appointment : null

  const [customerMode, setCustomerMode] = useState<CustomerMode>(() =>
    appointment !== null && appointment.customerId === null ? 'walkin' : 'existing',
  )
  const [customerQuery, setCustomerQuery] = useState('')
  const [appliedQuery, setAppliedQuery] = useState('')
  const [selectedCustomer, setSelectedCustomer] = useState<CustomerSummary | null>(() =>
    booking.kind === 'create' ? booking.customer ?? null : null,
  )
  const [newCustomer, setNewCustomer] = useState({ firstName: '', lastName: '', phoneNumber: '' })
  const [walkIn, setWalkIn] = useState({
    name: appointment?.customerName ?? '',
    phone: appointment?.customerPhone ?? '',
  })

  const [lines, setLines] = useState<DraftLine[]>(() =>
    appointment === null
      ? []
      : appointment.services.map(line => ({
          serviceId: line.serviceId,
          serviceName: line.serviceName,
          durationMinutes: line.durationMinutes,
          unitPrice: line.unitPrice,
          staffId: line.staffId,
          eligible: [],
        })),
  )
  const [serviceQuery, setServiceQuery] = useState('')
  const [menu, setMenu] = useState<ServiceSummary[] | undefined>(undefined)
  const [bundles, setBundles] = useState<Bundle[] | undefined>(undefined)
  const [bundleId, setBundleId] = useState<string | null>(appointment?.serviceBundleId ?? null)

  const [dateKey, setDateKey] = useState(() =>
    booking.kind === 'create'
      ? dayKeyOf(booking.day)
      : dayKeyOf(new Date(booking.appointment.startsAt)),
  )
  const [timeValue, setTimeValue] = useState(() =>
    booking.kind === 'create'
      ? defaultTimeValue(booking.startMinutes)
      : formatClockOf(new Date(booking.appointment.startsAt)),
  )
  const [notes, setNotes] = useState(appointment?.notes ?? '')

  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)
  const [duplicateWarning, setDuplicateWarning] = useState<string>()
  const [availability, setAvailability] = useState<{
    window: AvailabilityWindow
    isAvailable: boolean
  } | null>(null)

  const eligibleRequested = useRef(new Set<string>())

  // Bookable menu, fetched once per open so the picker stays snappy; the
  // search box refilters the cached list client-side. Bundles are left out
  // because they get their own picker below — a bundle books as a whole, so
  // clicking one here would send its id as a single service line.
  useEffect(() => {
    let cancelled = false
    searchIndividualServices({ bookableOnly: true, status: 'Active', pageSize: 100 })
      .then(result => {
        if (!cancelled) {
          setMenu(result.items)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setMenu([])
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    listBookableBundles()
      .then(result => {
        if (!cancelled) {
          setBundles(result)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setBundles([])
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  const ensureEligibleStaff = useCallback(
    (serviceId: string): void => {
      if (eligibleRequested.current.has(serviceId)) {
        return
      }
      eligibleRequested.current.add(serviceId)
      getService(serviceId)
        .then(service =>
          setLines(current =>
            current.map(line => {
              if (line.serviceId !== serviceId) {
                return line
              }
              // A freshly added line has no pick yet: prefer the staff whose
              // column was clicked, else the first member allowed on the service.
              const fallback =
                booking.kind === 'create' && booking.staffId !== null
                  ? booking.staffId
                  : service.eligibleStaff[0]?.staffId ?? null
              const staffId =
                line.staffId ??
                (service.eligibleStaff.some(member => member.staffId === fallback) ? fallback : null)
              return { ...line, eligible: service.eligibleStaff, staffId }
            }),
          ),
        )
        .catch(() => {
          eligibleRequested.current.delete(serviceId)
        })
    },
    [booking],
  )

  // Edit mode starts with staff picks but no eligibility lists; fill them so
  // the per-line staff selects can offer every member allowed on the service.
  useEffect(() => {
    if (appointment === null) {
      return
    }
    for (const line of appointment.services) {
      ensureEligibleStaff(line.serviceId)
    }
  }, [appointment, ensureEligibleStaff])

  function addService(service: ServiceSummary): void {
    ensureEligibleStaff(service.id)
    setLines(current => [
      ...current,
      {
        serviceId: service.id,
        serviceName: service.name,
        durationMinutes: service.durationMinutes,
        unitPrice: service.price,
        staffId: null,
        eligible: [],
      },
    ])
  }

  function setLineStaff(serviceId: string, staffId: string): void {
    setLines(current =>
      current.map(line => (line.serviceId === serviceId ? { ...line, staffId } : line)),
    )
  }

  // A line only carries its staff list while it is on the form, so dropping one
  // also drops the request that filled it — otherwise adding that service again
  // would be taken as already answered and leave its staff select empty.
  function removeLine(serviceId: string): void {
    eligibleRequested.current.delete(serviceId)
    setLines(current => current.filter(line => line.serviceId !== serviceId))
  }

  /**
   * A bundle books as one appointment whose lines are exactly its components, so
   * applying one replaces whatever was picked and the individual menu goes away
   * — the API rejects any other set with "submit exactly those services".
   */
  function applyBundle(bundle: Bundle): void {
    const components = [...bundle.items].sort((a, b) => a.displayOrder - b.displayOrder)
    for (const item of components) {
      ensureEligibleStaff(item.serviceId)
    }
    setLines(
      components.map(item => ({
        serviceId: item.serviceId,
        serviceName: item.name,
        durationMinutes: item.durationMinutes,
        unitPrice: item.price,
        staffId: null,
        eligible: [],
      })),
    )
    setBundleId(bundle.id)
  }

  function clearBundle(): void {
    eligibleRequested.current.clear()
    setBundleId(null)
    setLines([])
  }

  const totalMinutes = lines.reduce((sum, line) => sum + line.durationMinutes, 0)
  const totalPrice = lines.reduce((sum, line) => sum + line.unitPrice, 0)

  // In edit mode the booked bundle may have gone unbookable since, so it is no
  // longer in the list; the appointment's own snapshots keep the header honest.
  const selectedBundle = bundles?.find(bundle => bundle.id === bundleId) ?? null
  const bundleName = selectedBundle?.name ?? appointment?.bundleName ?? null
  const bundlePrice = selectedBundle?.price ?? appointment?.bundlePrice ?? null
  const sharedStaffId = useMemo(() => {
    const ids = new Set(lines.map(line => line.staffId).filter((id): id is string => id !== null))
    return ids.size === 1 ? [...ids][0] : null
  }, [lines])

  const availabilityWindow = useMemo<AvailabilityWindow | null>(() => {
    const start = composeDateTime(dateKey, timeValue)
    if (sharedStaffId === null || start === null || totalMinutes === 0) {
      return null
    }
    return { staffId: sharedStaffId, start, durationMinutes: totalMinutes }
  }, [dateKey, timeValue, sharedStaffId, totalMinutes])

  // Availability hint for the single-staff case. It is a courtesy read, not a
  // gate: the save endpoint revalidates and its 409 is the ruling.
  useEffect(() => {
    if (availabilityWindow === null) {
      return
    }

    let cancelled = false
    const end = new Date(
      availabilityWindow.start.getTime() + availabilityWindow.durationMinutes * 60_000,
    )
    checkStaffAvailability(availabilityWindow.staffId, availabilityWindow.start, end)
      .then(result => {
        if (!cancelled) {
          setAvailability({ window: availabilityWindow, isAvailable: result.isAvailable })
        }
      })
      .catch(() => {
        // A failed courtesy check leaves the hint hidden; save still rules.
      })
    return () => {
      cancelled = true
    }
  }, [availabilityWindow])

  const availabilityHint =
    availability !== null && availability.window === availabilityWindow
      ? availability.isAvailable
      : undefined

  const groupedMenu = useMemo(() => {
    const needle = serviceQuery.trim().toLowerCase()
    const visible = (menu ?? []).filter(
      service => needle === '' || service.name.toLowerCase().includes(needle),
    )
    const groups = new Map<string, ServiceSummary[]>()

    for (const service of visible) {
      const key = service.categoryName ?? 'Other'
      const bucket = groups.get(key) ?? []
      bucket.push(service)
      groups.set(key, bucket)
    }

    return [...groups.entries()]
  }, [menu, serviceQuery])

  const customers = useCustomerSearch(appliedQuery)

  async function buildSaveRequest(allowDuplicatePhone: boolean): Promise<SaveAppointmentRequest> {
    let customerId: string | null = null
    let walkInCustomerName: string | null = null
    let walkInCustomerPhone: string | null = null

    if (customerMode === 'existing') {
      customerId = selectedCustomer?.id ?? appointment?.customerId ?? null
      if (customerId === null) {
        throw new ClientValidationError('Choose a customer, or switch to New customer / Walk-in.')
      }
    } else if (customerMode === 'new') {
      const request: SaveCustomerRequest = {
        firstName: newCustomer.firstName,
        lastName: newCustomer.lastName,
        phoneNumber: newCustomer.phoneNumber,
        email: null,
        dateOfBirth: null,
        gender: null,
        address: null,
        isActive: true,
        allowDuplicatePhone,
      }
      const created = await createCustomer(request)
      customerId = created.id
    } else {
      walkInCustomerName = walkIn.name.trim() === '' ? null : walkIn.name.trim()
      walkInCustomerPhone = walkIn.phone.trim() === '' ? null : walkIn.phone.trim()
    }

    const start = composeDateTime(dateKey, timeValue)
    if (start === null) {
      throw new ClientValidationError('Pick a date and a start time.')
    }

    if (lines.length === 0) {
      throw new ClientValidationError('Add at least one service.')
    }

    const services: AppointmentServiceLineInput[] = lines.map(line => {
      if (line.staffId === null) {
        throw new ClientValidationError(`Choose a staff member for ${line.serviceName}.`)
      }
      return { serviceId: line.serviceId, staffId: line.staffId }
    })

    return {
      customerId,
      walkInCustomerName,
      walkInCustomerPhone,
      startsAt: start.toISOString(),
      services,
      notes: notes.trim() === '' ? null : notes.trim(),
      serviceBundleId: bundleId,
    }
  }

  async function save(allowDuplicatePhone: boolean): Promise<void> {
    setFailure(undefined)
    setFieldErrors({})
    setIsSaving(true)

    try {
      const body = await buildSaveRequest(allowDuplicatePhone)
      if (appointment === null) {
        await createAppointment(body)
      } else {
        await updateAppointment(appointment.id, body)
      }
      onSaved()
    } catch (error) {
      // The 409 on a new customer names the existing one; park the message in
      // a dialog and let the receptionist decide whether to save anyway.
      if (error instanceof ClientValidationError) {
        setFailure(error.message)
      } else if (error instanceof ApiError && error.status === 409 && customerMode === 'new') {
        setDuplicateWarning(error.message)
      } else if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        setFieldErrors(error.errors)
      } else {
        setFailure(describeFailure(error))
      }
    } finally {
      setIsSaving(false)
    }
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    void save(false)
  }

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div
        className="dialog booking-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="booking-dialog-title"
        onClick={event => event.stopPropagation()}
      >
        <div className="booking-head">
          <h3 id="booking-dialog-title">{isEdit ? 'Edit appointment' : 'New appointment'}</h3>
          <button type="button" className="button booking-close" onClick={onClose} aria-label="Close booking dialog">
            ×
          </button>
        </div>

        {failure !== undefined && (
          <p className="banner" role="alert">{failure}</p>
        )}

        <form className="booking-form" onSubmit={submit} noValidate>
          <fieldset className="booking-section">
            <legend>Customer</legend>
            <div className="segmented" role="radiogroup" aria-label="Customer type">
              {CUSTOMER_MODES.map(mode => (
                <button
                  key={mode.value}
                  type="button"
                  className={customerMode === mode.value ? 'segment is-active' : 'segment'}
                  aria-pressed={customerMode === mode.value}
                  onClick={() => setCustomerMode(mode.value)}
                >
                  {mode.label}
                </button>
              ))}
            </div>

            {customerMode === 'existing' && (
              <div className="booking-customer-search">
                <div className="booking-inline">
                  <input
                    type="search"
                    value={customerQuery}
                    onChange={event => setCustomerQuery(event.target.value)}
                    onKeyDown={event => {
                      if (event.key === 'Enter') {
                        event.preventDefault()
                        setAppliedQuery(customerQuery.trim())
                      }
                    }}
                    placeholder="Search name or phone"
                    aria-label="Search customers"
                  />
                  <button
                    type="button"
                    className="button"
                    onClick={() => setAppliedQuery(customerQuery.trim())}
                  >
                    Search
                  </button>
                </div>
                {customers !== undefined && (
                  <ul className="booking-results">
                    {customers.length === 0 && <li className="booking-empty">No customers found.</li>}
                    {customers.map(customer => (
                      <li key={customer.id}>
                        <button
                          type="button"
                          className={
                            selectedCustomer?.id === customer.id
                              ? 'booking-result is-selected'
                              : 'booking-result'
                          }
                          onClick={() => setSelectedCustomer(customer)}
                        >
                          <span>
                            {customer.firstName} {customer.lastName}
                          </span>
                          {customer.phoneNumber !== null && <span>{customer.phoneNumber}</span>}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                {selectedCustomer !== null && (
                  <p className="booking-selected">
                    Booking for{' '}
                    <strong>
                      {selectedCustomer.firstName} {selectedCustomer.lastName}
                    </strong>
                    {selectedCustomer.phoneNumber !== null && ` · ${selectedCustomer.phoneNumber}`}
                  </p>
                )}
              </div>
            )}

            {customerMode === 'new' && (
              <div className="booking-grid">
                <label className="field">
                  First name
                  <input
                    value={newCustomer.firstName}
                    onChange={event =>
                      setNewCustomer(current => ({ ...current, firstName: event.target.value }))
                    }
                    maxLength={100}
                  />
                  <FieldErrors messages={fieldErrors.firstName} />
                </label>
                <label className="field">
                  Last name
                  <input
                    value={newCustomer.lastName}
                    onChange={event =>
                      setNewCustomer(current => ({ ...current, lastName: event.target.value }))
                    }
                    maxLength={100}
                  />
                  <FieldErrors messages={fieldErrors.lastName} />
                </label>
                <label className="field">
                  Mobile
                  <input
                    type="tel"
                    value={newCustomer.phoneNumber}
                    onChange={event =>
                      setNewCustomer(current => ({ ...current, phoneNumber: event.target.value }))
                    }
                    maxLength={30}
                  />
                  <FieldErrors messages={fieldErrors.phoneNumber} />
                </label>
              </div>
            )}

            {customerMode === 'walkin' && (
              <div className="booking-grid">
                <label className="field">
                  Name
                  <input
                    value={walkIn.name}
                    onChange={event => setWalkIn(current => ({ ...current, name: event.target.value }))}
                    maxLength={100}
                    placeholder="Walk-in guest"
                  />
                </label>
                <label className="field">
                  Phone (optional)
                  <input
                    type="tel"
                    value={walkIn.phone}
                    onChange={event => setWalkIn(current => ({ ...current, phone: event.target.value }))}
                    maxLength={30}
                  />
                </label>
              </div>
            )}
          </fieldset>

          <fieldset className="booking-section">
            <legend>Services</legend>

            {bundleId === null ? (
              <>
                {bundles !== undefined && bundles.length > 0 && (
                  <div className="booking-bundles">
                    <h4>Bundles</h4>
                    <ul>
                      {bundles.map(bundle => (
                        <li key={bundle.id}>
                          <button
                            type="button"
                            className="booking-service"
                            onClick={() => applyBundle(bundle)}
                          >
                            <span className="booking-service-name">{bundle.name}</span>
                            <span className="booking-service-meta">
                              {bundle.items.length} services · {formatDuration(bundle.durationMinutes)}{' '}
                              · {formatPrice(bundle.price)}
                              {bundle.savingsAmount > 0 && ` · save ${formatPrice(bundle.savingsAmount)}`}
                            </span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <input
                  type="search"
                  value={serviceQuery}
                  onChange={event => setServiceQuery(event.target.value)}
                  placeholder="Search services"
                  aria-label="Search services"
                />
                {menu === undefined ? (
                  <p className="booking-empty">Loading services…</p>
                ) : (
                  <div className="booking-menu">
                    {groupedMenu.length === 0 && <p className="booking-empty">No bookable services.</p>}
                    {groupedMenu.map(([category, services]) => (
                      <div key={category} className="booking-category">
                        <h4>{category}</h4>
                        <ul>
                          {services.map(service => (
                            <li key={service.id}>
                              <button
                                type="button"
                                className="booking-service"
                                onClick={() => addService(service)}
                              >
                                <span className="booking-service-name">{service.name}</span>
                                <span className="booking-service-meta">
                                  {formatDuration(service.durationMinutes)} · {formatPrice(service.price)}
                                </span>
                              </button>
                            </li>
                          ))}
                        </ul>
                      </div>
                    ))}
                  </div>
                )}
              </>
            ) : (
              <div className="booking-bundle-applied">
                <div className="booking-line-main">
                  <span className="booking-service-name">{bundleName}</span>
                  <span className="booking-service-meta">
                    {lines.length} services · {formatDuration(totalMinutes)}
                    {bundlePrice !== null && ` · ${formatPrice(bundlePrice)}`}
                  </span>
                </div>
                <button type="button" className="button button-compact" onClick={clearBundle}>
                  Remove bundle
                </button>
              </div>
            )}

            {lines.length > 0 && (
              <ul className="booking-lines">
                {lines.map(line => (
                  <li key={line.serviceId} className="booking-line">
                    <div className="booking-line-main">
                      <span className="booking-service-name">{line.serviceName}</span>
                      <span className="booking-service-meta">
                        {formatDuration(line.durationMinutes)} · {formatPrice(line.unitPrice)}
                      </span>
                    </div>
                    <label className="booking-line-staff">
                      Staff
                      <select
                        value={line.staffId ?? ''}
                        onChange={event => setLineStaff(line.serviceId, event.target.value)}
                      >
                        <option value="" disabled>
                          Choose staff
                        </option>
                        {line.eligible.map(member => (
                          <option key={member.staffId} value={member.staffId}>
                            {member.firstName} {member.lastName}
                          </option>
                        ))}
                      </select>
                    </label>
                    {bundleId === null && (
                      <button
                        type="button"
                        className="button booking-remove"
                        onClick={() => removeLine(line.serviceId)}
                        aria-label={`Remove ${line.serviceName}`}
                      >
                        ×
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}

            <FieldErrors messages={fieldErrors.services} />
            <FieldErrors messages={fieldErrors.serviceBundleId} />

            {lines.length > 0 &&
              (bundleId !== null && bundlePrice !== null ? (
                <p className="booking-total">
                  Bundle price {formatPrice(bundlePrice)} · {formatDuration(totalMinutes)}
                  {totalPrice > bundlePrice && (
                    <span className="booking-saving">saves {formatPrice(totalPrice - bundlePrice)}</span>
                  )}
                </p>
              ) : (
                <p className="booking-total">
                  Total {formatDuration(totalMinutes)} · {formatPrice(totalPrice)}
                </p>
              ))}
          </fieldset>

          <fieldset className="booking-section">
            <legend>Date &amp; time</legend>
            <div className="booking-grid">
              <label className="field">
                Date
                <input
                  type="date"
                  value={dateKey}
                  onChange={event => setDateKey(event.target.value)}
                />
              </label>
              <label className="field">
                Start time
                <input
                  type="time"
                  step={300}
                  value={timeValue}
                  onChange={event => setTimeValue(event.target.value)}
                />
              </label>
            </div>
            {availabilityHint !== undefined && (
              <p className={availabilityHint ? 'booking-hint is-ok' : 'booking-hint is-blocked'}>
                {availabilityHint
                  ? 'This staff member is available for the selected window.'
                  : 'This staff member is not available for the selected window — the booking may be rejected.'}
              </p>
            )}
          </fieldset>

          <label className="field">
            Notes
            <textarea
              value={notes}
              onChange={event => setNotes(event.target.value)}
              maxLength={2000}
              rows={2}
            />
          </label>

          <div className="form-actions">
            <button type="submit" className="button button-primary" disabled={isSaving}>
              {isSaving ? 'Saving…' : isEdit ? 'Save appointment' : 'Book appointment'}
            </button>
            <button type="button" className="button" onClick={onClose} disabled={isSaving}>
              Cancel
            </button>
          </div>
        </form>

        {duplicateWarning !== undefined && (
          <ConfirmationDialog
            title="Possible duplicate customer"
            confirmLabel="Save anyway"
            busy={isSaving}
            onConfirm={() => {
              setDuplicateWarning(undefined)
              void save(true)
            }}
            onCancel={() => setDuplicateWarning(undefined)}
          >
            <p>{duplicateWarning}</p>
          </ConfirmationDialog>
        )}
      </div>
    </div>
  )
}

/**
 * Customer lookup driven by the applied query; an empty query keeps the list
 * closed until the receptionist searches, matching the list pages. Results are
 * tagged with the query they answer so a stale response never renders.
 */
function useCustomerSearch(query: string): CustomerSummary[] | undefined {
  const [results, setResults] = useState<{ query: string; items: CustomerSummary[] } | null>(null)

  useEffect(() => {
    if (query === '') {
      return
    }

    let cancelled = false
    searchCustomers({ search: query, status: 'Active', pageSize: 8 })
      .then(page => {
        if (!cancelled) {
          setResults({ query, items: page.items })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResults({ query, items: [] })
        }
      })
    return () => {
      cancelled = true
    }
  }, [query])

  return results !== null && results.query === query ? results.items : undefined
}
