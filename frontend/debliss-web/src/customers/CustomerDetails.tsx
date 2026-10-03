import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import { setCustomerStatus } from '../api/customers.ts'
import { getCustomer } from '../api/customers.ts'
import { useAsync } from '../useAsync.ts'
import { StatusPill } from '../components/StatusPill.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { formatDate } from '../format.ts'
import { CustomerHeader } from './CustomerHeader.tsx'
import { CustomerSummary } from './CustomerSummary.tsx'
import { CustomerNotes } from './CustomerNotes.tsx'
import { CustomerAppointmentHistory } from './CustomerAppointmentHistory.tsx'

interface CustomerDetailsProps {
  customerId: string
  onBack: () => void
  onEdit: (customerId: string) => void
}

/**
 * The full profile: contact details, the appointment counts read from the
 * scheduling tables, free-form notes, and appointment history. The summary
 * and history panels are where later modules (packages, loyalty, spending)
 * hang additional sections without touching this layout.
 */
export function CustomerDetails({ customerId, onBack, onEdit }: CustomerDetailsProps) {
  const customer = useAsync(() => getCustomer(customerId), [customerId])
  const [actionFailure, setActionFailure] = useState<string>()

  function toggleStatus() {
    if (customer.data === undefined) {
      return
    }

    setActionFailure(undefined)
    setCustomerStatus(customer.data.id, !customer.data.isActive)
      .then(() => customer.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (customer.failure !== undefined) {
    return (
      <section className="panel" aria-label="Customer profile">
        <div className="panel-header">
          <h2>Customer profile</h2>
        </div>
        <ErrorState message={customer.failure} onRetry={customer.reload} />
        <div className="form-actions">
          <button type="button" className="button" onClick={onBack}>
            Back to customers
          </button>
        </div>
      </section>
    )
  }

  if (customer.data === undefined) {
    return (
      <section className="panel" aria-label="Customer profile">
        <div className="panel-header">
          <h2>Customer profile</h2>
        </div>
        <LoadingState label="Loading profile…" />
      </section>
    )
  }

  const profile = customer.data

  return (
    <>
      <section className="panel" aria-label="Customer profile">
        <CustomerHeader
          customer={profile}
          onEdit={onEdit}
          onToggleStatus={toggleStatus}
          onBack={onBack}
        />

        {actionFailure !== undefined && (
          <p className="banner" role="alert">{actionFailure}</p>
        )}
      </section>

      <section className="panel" aria-label="Customer information">
        <div className="panel-header">
          <h2>Customer Information</h2>
        </div>
        <dl className="detail-grid">
          <dt>Phone</dt>
          <dd>{profile.phoneNumber ?? '—'}</dd>
          <dt>Email</dt>
          <dd>{profile.email ?? '—'}</dd>
          <dt>Date of birth</dt>
          <dd>{profile.dateOfBirth === null ? '—' : formatDate(profile.dateOfBirth)}</dd>
          <dt>Gender</dt>
          <dd>{profile.gender ?? '—'}</dd>
          <dt>Address</dt>
          <dd>{profile.address ?? '—'}</dd>
          <dt>Customer since</dt>
          <dd>{formatDate(profile.createdAt)}</dd>
          <dt>Status</dt>
          <dd><StatusPill isActive={profile.isActive} /></dd>
        </dl>
      </section>

      <CustomerSummary summary={profile.appointmentSummary} />
      <CustomerNotes customerId={profile.id} />
      <CustomerAppointmentHistory customerId={profile.id} />
    </>
  )
}
