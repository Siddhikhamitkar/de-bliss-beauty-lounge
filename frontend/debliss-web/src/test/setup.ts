import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// React Testing Library only auto-cleans when test globals are enabled; this
// project imports vitest's helpers explicitly, so cleanup is wired up here.
afterEach(cleanup)
