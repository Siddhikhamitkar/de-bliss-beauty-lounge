import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { RowActionMenu } from './RowActionMenu.tsx'

describe('RowActionMenu', () => {
  it('opens on the trigger and runs an action on selection', async () => {
    const user = userEvent.setup()
    const onDeactivate = vi.fn()
    render(
      <RowActionMenu
        label="Actions for Lina Mansour"
        actions={[{ label: 'Deactivate', onSelect: onDeactivate, isDanger: true }]}
      />,
    )

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Actions for Lina Mansour' }))
    expect(screen.getByRole('menu')).toBeInTheDocument()

    await user.click(screen.getByRole('menuitem', { name: 'Deactivate' }))

    expect(onDeactivate).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('closes on Escape without leaking the key to the page behind', async () => {
    const user = userEvent.setup()
    const behind = vi.fn()
    document.addEventListener('keydown', behind)

    try {
      render(
        <RowActionMenu label="Actions" actions={[{ label: 'Deactivate', onSelect: vi.fn() }]} />,
      )

      await user.click(screen.getByRole('button', { name: 'Actions' }))
      await user.keyboard('{Escape}')

      expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      expect(behind).not.toHaveBeenCalled()
    } finally {
      document.removeEventListener('keydown', behind)
    }
  })

  it('closes when clicking outside and stops clicks from reaching the row', async () => {
    const user = userEvent.setup()
    const onRowClick = vi.fn()
    render(
      <div onClick={onRowClick}>
        <RowActionMenu label="Actions" actions={[{ label: 'Deactivate', onSelect: vi.fn() }]} />
      </div>,
    )

    await user.click(screen.getByRole('button', { name: 'Actions' }))
    expect(onRowClick).not.toHaveBeenCalled()

    await user.click(document.body)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })
})
