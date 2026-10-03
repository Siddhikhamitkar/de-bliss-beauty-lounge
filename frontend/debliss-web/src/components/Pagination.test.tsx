import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Pagination } from './Pagination.tsx'

const pageOneOfTwo = {
  items: [],
  totalCount: 59,
  page: 1,
  pageSize: 50,
  totalPages: 2,
  hasPreviousPage: false,
  hasNextPage: true,
}

describe('Pagination', () => {
  it('summarises the page and total', () => {
    render(<Pagination result={pageOneOfTwo} onChange={() => {}} />)
    expect(screen.getByText('Page 1 of 2 · 59 in total')).toBeInTheDocument()
  })

  it('disables Previous on the first page and Next on the last', () => {
    render(<Pagination result={pageOneOfTwo} onChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()
  })

  it('asks for the neighbouring page', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Pagination result={pageOneOfTwo} onChange={onChange} />)

    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(onChange).toHaveBeenCalledWith(2)
  })
})
