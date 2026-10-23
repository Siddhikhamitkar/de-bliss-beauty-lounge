import { useAuth } from '../auth/AuthContext.tsx'

/**
 * Staff landing. Staff authenticate today and can reach their own dashboard;
 * the workspace modules for them are defined by a later task, so this stays
 * a safe placeholder — nothing here leaks owner or receptionist tools.
 */
export function StaffDashboardPage() {
  const { user } = useAuth()

  return (
    <section className="dash" aria-label="Staff dashboard">
      <header className="dash-hero">
        <div className="dash-hero-text">
          <p className="dash-hero-eyebrow">De Bliss Beauty Lounge</p>
          <h1 className="dash-greeting">
            Welcome to De Bliss, {user?.firstName ?? 'there'}
          </h1>
          <p className="dash-subline">Your staff workspace is being prepared.</p>
        </div>
      </header>

      <div className="placeholder-card">
        <p className="placeholder-title">Your tools are on the way</p>
        <p className="placeholder-body">
          Your daily schedule and client notes will live here. Your sign-in already works —
          check back once the staff workspace ships.
        </p>
      </div>
    </section>
  )
}
