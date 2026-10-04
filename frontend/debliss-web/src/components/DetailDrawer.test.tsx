import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DetailDrawer, DrawerHeader, DrawerSection } from './DetailDrawer.tsx'

function renderDrawer(onClose: () => void) {
  return render(
    <DetailDrawer label="Customer details" onClose={onClose}>
      <DrawerHeader
        title="Lina Mansour"
        subtitle="050 123 4567"
        avatar={<span className="avatar">LM</span>}
        actions={
          <button type="button" className="button">
            Edit
          </button>
        }
        onClose={onClose}
      />
      <DrawerSection title="Overview">
        <p>Customer since 2024</p>
      </DrawerSection>
    </DetailDrawer>,
  )
}

describe('DetailDrawer', () => {
  it('renders as a labelled dialog with header content and sections', () => {
    renderDrawer(vi.fn())

    expect(screen.getByRole('dialog', { name: 'Customer details' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Lina Mansour' })).toBeInTheDocument()
    expect(screen.getByText('050 123 4567')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Overview' })).toBeInTheDocument()
    expect(screen.getByText('Customer since 2024')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument()
  })

  it('closes from the close button', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    renderDrawer(onClose)

    await user.click(screen.getByRole('button', { name: 'Close' }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('closes on backdrop click but not on a click inside the panel', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    const { container } = renderDrawer(onClose)

    await user.click(screen.getByText('Customer since 2024'))
    expect(onClose).not.toHaveBeenCalled()

    const backdrop = container.querySelector('.drawer-backdrop')
    expect(backdrop).not.toBeNull()
    await user.click(backdrop as HTMLElement)

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('closes on Escape', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    renderDrawer(onClose)

    await user.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('leaves Escape alone while a field is being edited', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(
      <DetailDrawer label="Customer details" onClose={onClose}>
        <input aria-label="First name" />
      </DetailDrawer>,
    )

    await user.click(screen.getByRole('textbox', { name: 'First name' }))
    await user.keyboard('{Escape}')

    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('textbox', { name: 'First name' })).toBeInTheDocument()
  })

  it('defers to a stacked confirmation dialog', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(
      <DetailDrawer label="Customer details" onClose={onClose}>
        <div className="dialog-backdrop">
          <p>Are you sure?</p>
        </div>
      </DetailDrawer>,
    )

    await user.keyboard('{Escape}')

    expect(onClose).not.toHaveBeenCalled()
  })

  it('uses the wide panel when asked', () => {
    render(
      <DetailDrawer label="Staff details" onClose={vi.fn()} isWide>
        <p>Content</p>
      </DetailDrawer>,
    )

    expect(screen.getByRole('dialog', { name: 'Staff details' })).toHaveClass('drawer-wide')
  })

  it('adds the extra panel class alongside the base one', () => {
    render(
      <DetailDrawer label="Customer details" onClose={vi.fn()} panelClassName="drawer-customer">
        <p>Content</p>
      </DetailDrawer>,
    )

    expect(screen.getByRole('dialog', { name: 'Customer details' })).toHaveClass(
      'drawer',
      'drawer-customer',
    )
  })
})
