import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StockStatusPill } from './StockStatusPill.tsx'

describe('StockStatusPill', () => {
  it('labels each stock level with the hue class that matches it', () => {
    const { rerender } = render(<StockStatusPill status="InStock" />)
    expect(screen.getByText('In stock')).toHaveClass('status-InStock')

    rerender(<StockStatusPill status="LowStock" />)
    expect(screen.getByText('Low stock')).toHaveClass('status-LowStock')

    rerender(<StockStatusPill status="OutOfStock" />)
    expect(screen.getByText('Out of stock')).toHaveClass('status-OutOfStock')
  })
})
