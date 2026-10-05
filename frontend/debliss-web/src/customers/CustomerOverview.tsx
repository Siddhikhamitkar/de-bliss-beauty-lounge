import type { CustomerProfile } from '../api/contracts.ts'
import { formatDate } from '../format.ts'
import { CustomerSummary } from './CustomerSummary.tsx'
import { CustomerWhatsAppOptIn } from './CustomerWhatsAppOptIn.tsx'

interface CustomerOverviewProps {
  profile: CustomerProfile
  onChanged: () => void
}

/**
 * The at-a-glance tab. It repeats nothing from the header: the customer's
 * phone and status already sit there, so only the relationship's bookends —
 * when they joined and how to reach them by mail — show up again here. The
 * WhatsApp consent card sits beside the summary because recording it is a
 * front-desk, at-a-glance task.
 */
export function CustomerOverview({ profile, onChanged }: CustomerOverviewProps) {
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

      <CustomerWhatsAppOptIn profile={profile} onChanged={onChanged} />
    </>
  )
}
