import { useState } from 'react'
import { CategoriesPage } from './categories/CategoriesPage.tsx'
import { ServicesPage } from './services/ServicesPage.tsx'
import { StaffPage } from './staff/StaffPage.tsx'

type Section = 'services' | 'categories' | 'staff'

function App() {
  const [section, setSection] = useState<Section>('services')

  return (
    <div className="app">
      <header className="app-header">
        <div>
          <h1>De Bliss Beauty Lounge</h1>
          <p className="app-subtitle">Salon manager</p>
        </div>
        <nav className="app-nav" aria-label="Sections">
          <button
            type="button"
            className="nav-button"
            aria-current={section === 'services' ? 'page' : undefined}
            onClick={() => setSection('services')}
          >
            Services
          </button>
          <button
            type="button"
            className="nav-button"
            aria-current={section === 'categories' ? 'page' : undefined}
            onClick={() => setSection('categories')}
          >
            Categories
          </button>
          <button
            type="button"
            className="nav-button"
            aria-current={section === 'staff' ? 'page' : undefined}
            onClick={() => setSection('staff')}
          >
            Staff
          </button>
        </nav>
      </header>

      {section === 'services' && <ServicesPage />}
      {section === 'categories' && <CategoriesPage />}
      {section === 'staff' && <StaffPage />}
    </div>
  )
}

export default App
