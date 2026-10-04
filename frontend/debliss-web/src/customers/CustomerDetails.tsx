import type { CustomerProfile } from '../api/contracts.ts'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDate } from '../format.ts'

interface CustomerDetailsProps {
  profile: CustomerProfile
  onEdit: () => void
}

/** The full stored record, one quiet label/value row per field. */
export function CustomerDetails({ profile, onEdit }: CustomerDetailsProps) {
  return (
    <section className="tab-section" aria-label="Customer details">
      <div className="tab-section-head">
        <h4>Customer details</h4>
        <button type="button" className="button" onClick={onEdit}>
          Edit
        </button>
      </div>
      <dl className="kv-grid">
        <dt>First name</dt>
        <dd>{profile.firstName}</dd>
        <dt>Last name</dt>
        <dd>{profile.lastName}</dd>
        <dt>Mobile</dt>
        <dd>{profile.phoneNumber ?? '—'}</dd>
        <dt>Email</dt>
        <dd>{profile.email ?? '—'}</dd>
        <dt>Date of birth</dt>
        <dd>{profile.dateOfBirth === null ? '—' : formatDate(profile.dateOfBirth)}</dd>
        <dt>Gender</dt>
        <dd>{profile.gender ?? '—'}</dd>
        <dt>Address</dt>
        <dd>{profile.address ?? '—'}</dd>
        <dt>Status</dt>
        <dd><StatusPill isActive={profile.isActive} /></dd>
        <dt>Customer since</dt>
        <dd>{formatDate(profile.createdAt)}</dd>
      </dl>
    </section>
  )
}
