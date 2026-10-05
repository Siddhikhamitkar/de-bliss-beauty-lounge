import { useAuth } from '../auth/AuthContext.tsx'

/**
 * Owner landing. The workspace modules (reports, revenue, settings, user
 * administration) are deliberately NOT built yet — authorization protects
 * this route from day one, the information architecture arrives later.
 */
export function OwnerDashboardPage() {
  const { user } = useAuth()

  return (
    <section className="dash" aria-label="Owner dashboard">
      <header className="dash-hero">
        <div className="dash-hero-text">
          <p className="dash-hero-eyebrow">De Bliss Beauty Lounge</p>
          <h1 className="dash-greeting">
            Welcome to De Bliss, {user?.firstName ?? 'Owner'}{' '}
            <span className="dash-wave" aria-hidden="true">
              ✨
            </span>
          </h1>
          <p className="dash-subline">Your owner workspace is being prepared.</p>
        </div>
      </header>

      <div className="placeholder-card">
        <p className="placeholder-title">Owner tools are on the way</p>
        <p className="placeholder-body">
          Revenue analytics, reports, marketing and lounge settings will live here. In the
          meantime the full operational floor — dashboard, calendar, customers, services, staff
          and billing — is yours to use.
        </p>
      </div>
    </section>
  )
}
