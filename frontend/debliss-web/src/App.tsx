import { useEffect, useState } from 'react'
import { homePathFor, isKnownPath, isPathAllowed, navigateTo, usePathname } from './app/router.ts'
import type { CustomerSummary } from './api/contracts.ts'
import { AuthProvider, useAuth } from './auth/AuthContext.tsx'
import { LoginPage } from './auth/LoginPage.tsx'
import { BillingPage } from './billing/BillingPage.tsx'
import { CheckoutPage } from './billing/CheckoutPage.tsx'
import { CalendarPage } from './calendar/CalendarPage.tsx'
import type { PendingFocus } from './calendar/CalendarPage.tsx'
import { CategoriesPage } from './categories/CategoriesPage.tsx'
import { CouponsPage } from './coupons/CouponsPage.tsx'
import { CustomersPage } from './customers/CustomersPage.tsx'
import { DashboardPage } from './dashboard/DashboardPage.tsx'
import { OwnerDashboardPage } from './dashboard/OwnerDashboardPage.tsx'
import { StaffDashboardPage } from './dashboard/StaffDashboardPage.tsx'
import { AppSidebar } from './layout/AppSidebar.tsx'
import { InventoryPage } from './inventory/InventoryPage.tsx'
import { RemindersPage } from './reminders/RemindersPage.tsx'
import { ServicesPage } from './services/ServicesPage.tsx'
import { SettingsPage } from './settings/SettingsPage.tsx'
import { StaffPage } from './staff/StaffPage.tsx'

function App() {
  return (
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  )
}

/**
 * URL-driven shell. Guards run on every render against the current path:
 * anonymous users always end at /login; a signed-in user at /login, at an
 * unknown path, or at a path their roles do not admit is moved to their own
 * home. Everything else renders the matching module.
 */
function AppRoutes() {
  const { user, loading, logout } = useAuth()
  const path = usePathname()

  // Dashboard hand-offs picked up on mount by the module they land in.
  const [pendingBookingCustomer, setPendingBookingCustomer] = useState<CustomerSummary | null>(null)
  const [pendingFocus, setPendingFocus] = useState<PendingFocus | null>(null)
  const [pendingCustomerCreate, setPendingCustomerCreate] = useState(false)
  // Checkout is a full-screen workspace over whatever the user was doing.
  const [checkoutAppointmentId, setCheckoutAppointmentId] = useState<string | null>(null)
  // A walk-in retail draft opens checkout by invoice, not appointment.
  const [checkoutInvoiceId, setCheckoutInvoiceId] = useState<string | null>(null)
  // Bumped whenever a payment lands so dashboard and billing reload on next show.
  const [dataRevision, setDataRevision] = useState(0)

  if (loading) {
    return (
      <div className="app-splash" role="status">
        <span className="side-brand-mark" aria-hidden="true">
          DB
        </span>
        <p>Loading your workspace…</p>
      </div>
    )
  }

  if (user === null) {
    return path === '/login' ? <LoginPage /> : <Redirect to="/login" />
  }

  if (path === '/login' || !isKnownPath(path) || !isPathAllowed(path, user.roles)) {
    return <Redirect to={homePathFor(user.roles)} />
  }

  return (
    <div className="app-shell">
      <AppSidebar
        currentPath={path}
        user={user}
        onNavigate={navigateTo}
        onSignOut={() => {
          void logout()
        }}
      />

      <main className={path === '/calendar' ? 'app-main app-main-wide' : 'app-main'}>
        {path === '/receptionist/dashboard' && (
          <DashboardPage
            revision={dataRevision}
            firstName={user.firstName}
            onOpenAppointment={appointmentId => {
              setPendingFocus({ kind: 'details', appointmentId })
              navigateTo('/calendar')
            }}
            onNewAppointment={() => {
              setPendingFocus({ kind: 'create' })
              navigateTo('/calendar')
            }}
            onAddCustomer={() => {
              setPendingCustomerCreate(true)
              navigateTo('/customers')
            }}
            onFindCustomer={() => navigateTo('/customers')}
            onOpenBilling={() => navigateTo('/billing')}
            onCheckout={setCheckoutAppointmentId}
            onViewAllAppointments={() => navigateTo('/calendar')}
          />
        )}
        {path === '/owner/dashboard' && (
          <OwnerDashboardPage
            revision={dataRevision}
            firstName={user.firstName}
            onNavigate={navigateTo}
            onOpenAppointment={appointmentId => {
              setPendingFocus({ kind: 'details', appointmentId })
              navigateTo('/calendar')
            }}
            onCheckout={setCheckoutAppointmentId}
          />
        )}
        {path === '/staff/dashboard' && <StaffDashboardPage />}
        {path === '/calendar' && (
          <CalendarPage
            pendingBookingCustomer={pendingBookingCustomer}
            onPendingBookingHandled={() => setPendingBookingCustomer(null)}
            pendingFocus={pendingFocus}
            onPendingFocusHandled={() => setPendingFocus(null)}
            onCheckout={setCheckoutAppointmentId}
          />
        )}
        {path === '/services' && <ServicesPage onManageCategories={() => navigateTo('/categories')} />}
        {path === '/categories' && <CategoriesPage />}
        {path === '/staff' && <StaffPage canViewAttendance={user.roles.includes('OWNER')} />}
        {path === '/customers' && (
          <CustomersPage
            pendingCreate={pendingCustomerCreate}
            onPendingCreateHandled={() => setPendingCustomerCreate(false)}
            onBookAppointment={customer => {
              setPendingBookingCustomer(customer)
              navigateTo('/calendar')
            }}
          />
        )}
        {path === '/billing' && (
          <BillingPage
            revision={dataRevision}
            onResumeCheckout={setCheckoutInvoiceId}
            onStartRetailSale={setCheckoutInvoiceId}
            onManageCoupons={() => navigateTo('/coupons')}
          />
        )}
        {path === '/coupons' && <CouponsPage />}
        {path === '/inventory' && <InventoryPage canManageInventory={user.roles.includes('OWNER')} />}
        {path === '/reminders' && <RemindersPage />}
        {path === '/settings' && <SettingsPage />}
      </main>

      {checkoutAppointmentId !== null && (
        <CheckoutPage
          key={checkoutAppointmentId}
          appointmentId={checkoutAppointmentId}
          onClose={() => setCheckoutAppointmentId(null)}
          onPaid={() => setDataRevision(revision => revision + 1)}
        />
      )}

      {checkoutInvoiceId !== null && (
        <CheckoutPage
          key={checkoutInvoiceId}
          invoiceId={checkoutInvoiceId}
          onClose={() => setCheckoutInvoiceId(null)}
          onPaid={() => setDataRevision(revision => revision + 1)}
        />
      )}
    </div>
  )
}

/** Pushes the target path and lets the popstate listener re-render the shell. */
function Redirect({ to }: { to: string }) {
  useEffect(() => {
    navigateTo(to)
  }, [to])

  return null
}

export default App
