import { useState } from 'react'
import { CategoriesPage } from './categories/CategoriesPage.tsx'
import { ServicesPage } from './services/ServicesPage.tsx'

function App() {
  const [section, setSection] = useState<'services' | 'categories'>('services')

  return (
    <div className="app">
      <header className="app-header">
        <div>
          <h1>De Bliss Beauty Lounge</h1>
          <p className="app-subtitle">Services and pricing</p>
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
        </nav>
      </header>

      {section === 'services' ? <ServicesPage /> : <CategoriesPage />}
    </div>
  )
}

export default App
