import { useState } from 'react'
import { CalendarPage } from './calendar/CalendarPage.tsx'
import { CategoriesPage } from './categories/CategoriesPage.tsx'
import { CustomersPage } from './customers/CustomersPage.tsx'
import { AppSidebar } from './layout/AppSidebar.tsx'
import type { Section } from './layout/AppSidebar.tsx'
import { ServicesPage } from './services/ServicesPage.tsx'
import { StaffPage } from './staff/StaffPage.tsx'

function App() {
  // The calendar is the operational heart of the salon: it opens first.
  const [section, setSection] = useState<Section>('calendar')

  return (
    <div className="app-shell">
      <AppSidebar current={section} onNavigate={setSection} />

      <main className={section === 'calendar' ? 'app-main app-main-wide' : 'app-main'}>
        {section === 'calendar' && <CalendarPage />}
        {section === 'services' && (
          <ServicesPage onManageCategories={() => setSection('categories')} />
        )}
        {section === 'categories' && <CategoriesPage />}
        {section === 'staff' && <StaffPage />}
        {section === 'customers' && <CustomersPage />}
      </main>
    </div>
  )
}

export default App
