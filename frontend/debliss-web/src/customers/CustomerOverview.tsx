import type { CustomerProfile } from '../api/contracts.ts'
import { formatDate } from '../format.ts'
import { CustomerSummary } from './CustomerSummary.tsx'

interface CustomerOverviewProps {
  profile: CustomerProfile
}

/**
 * The at-a-glance tab. It repeats nothing from the header: the customer's
 * phone and status already sit there, so only the relationship's bookends —
 * when they joined and how to reach them by mail — show up again here.
 */
export function CustomerOverview({ profile }: CustomerOverviewProps) {
  return (
    <>
      <section className="tab-section" aria-label="Customer information">
        <div className="tab-section-head">
          <h4>Customer</h4>
        </div>
        <dl className="kv-grid">
          <dt>Customer since</dt>
          <dd>{formatDate(profile.createdAt)}</dd>
          <dt>Email</dt>
          <dd>{profile.email ?? '—'}</dd>
        </dl>
      </section>

      <CustomerSummary summary={profile.appointmentSummary} />
    </>
  )
}
