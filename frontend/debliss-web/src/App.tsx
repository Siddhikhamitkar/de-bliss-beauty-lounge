import { useState } from 'react'
import type { CustomerSummary } from './api/contracts.ts'
import { BillingPage } from './billing/BillingPage.tsx'
import { CheckoutPage } from './billing/CheckoutPage.tsx'
import { CalendarPage } from './calendar/CalendarPage.tsx'
import type { PendingFocus } from './calendar/CalendarPage.tsx'
import { CategoriesPage } from './categories/CategoriesPage.tsx'
import { CouponsPage } from './coupons/CouponsPage.tsx'
import { CustomersPage } from './customers/CustomersPage.tsx'
import { DashboardPage } from './dashboard/DashboardPage.tsx'
import { AppSidebar } from './layout/AppSidebar.tsx'
import type { Section } from './layout/AppSidebar.tsx'
import { ServicesPage } from './services/ServicesPage.tsx'
import { StaffPage } from './staff/StaffPage.tsx'

function App() {
  // The dashboard is the front desk's landing page; the calendar stays one
  // click away as the operational heart.
  const [section, setSection] = useState<Section>('dashboard')
  const [pendingBookingCustomer, setPendingBookingCustomer] = useState<CustomerSummary | null>(null)
  // Dashboard hand-offs picked up on mount by the section they land in.
  const [pendingFocus, setPendingFocus] = useState<PendingFocus | null>(null)
  const [pendingCustomerCreate, setPendingCustomerCreate] = useState(false)
  // Checkout is a full-screen workspace over whatever the receptionist was doing.
  const [checkoutAppointmentId, setCheckoutAppointmentId] = useState<string | null>(null)
  // Bumped whenever a payment lands so dashboard and billing reload on next show.
  const [dataRevision, setDataRevision] = useState(0)

  return (
    <div className="app-shell">
      <AppSidebar current={section} onNavigate={setSection} />

      <main className={section === 'calendar' ? 'app-main app-main-wide' : 'app-main'}>
        {section === 'dashboard' && (
          <DashboardPage
            revision={dataRevision}
            onOpenAppointment={appointmentId => {
              setPendingFocus({ kind: 'details', appointmentId })
              setSection('calendar')
            }}
            onNewAppointment={() => {
              setPendingFocus({ kind: 'create' })
              setSection('calendar')
            }}
            onAddCustomer={() => {
              setPendingCustomerCreate(true)
              setSection('customers')
            }}
            onFindCustomer={() => setSection('customers')}
            onOpenBilling={() => setSection('billing')}
            onCheckout={setCheckoutAppointmentId}
            onViewAllAppointments={() => setSection('calendar')}
          />
        )}
        {section === 'calendar' && (
          <CalendarPage
            pendingBookingCustomer={pendingBookingCustomer}
            onPendingBookingHandled={() => setPendingBookingCustomer(null)}
            pendingFocus={pendingFocus}
            onPendingFocusHandled={() => setPendingFocus(null)}
            onCheckout={setCheckoutAppointmentId}
          />
        )}
        {section === 'services' && (
          <ServicesPage onManageCategories={() => setSection('categories')} />
        )}
        {section === 'categories' && <CategoriesPage />}
        {section === 'staff' && <StaffPage />}
        {section === 'customers' && (
          <CustomersPage
            pendingCreate={pendingCustomerCreate}
            onPendingCreateHandled={() => setPendingCustomerCreate(false)}
            onBookAppointment={customer => {
              setPendingBookingCustomer(customer)
              setSection('calendar')
            }}
          />
        )}
        {section === 'billing' && (
          <BillingPage
            revision={dataRevision}
            onResumeCheckout={setCheckoutAppointmentId}
            onManageCoupons={() => setSection('coupons')}
          />
        )}
        {section === 'coupons' && <CouponsPage />}
      </main>

      {checkoutAppointmentId !== null && (
        <CheckoutPage
          key={checkoutAppointmentId}
          appointmentId={checkoutAppointmentId}
          onClose={() => setCheckoutAppointmentId(null)}
          onPaid={() => setDataRevision(revision => revision + 1)}
        />
      )}
    </div>
  )
}

export default App
