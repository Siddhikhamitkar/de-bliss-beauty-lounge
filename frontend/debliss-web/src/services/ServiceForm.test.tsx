import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import { createService, listCategories, listStaffOptions } from '../api/catalogue.ts'
import type { Service } from '../api/contracts.ts'
import { ServiceEditor } from './ServiceForm.tsx'

vi.mock('../api/catalogue.ts', () => ({
  createService: vi.fn(),
  getService: vi.fn(),
  listCategories: vi.fn(),
  listStaffOptions: vi.fn(),
  updateService: vi.fn(),
}))

function renderNewService(onSaved: () => void = () => {}) {
  return render(<ServiceEditor serviceId={null} onSaved={onSaved} onCancel={() => {}} />)
}

async function fillRequiredFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Name'), 'Gel Manicure')
  await user.type(screen.getByLabelText('Price'), '28')
}

describe('ServiceEditor', () => {
  beforeEach(() => {
    vi.mocked(listCategories).mockResolvedValue([])
    vi.mocked(listStaffOptions).mockResolvedValue([])
  })

  it('blocks the save and marks the price field when it is blank', async () => {
    const user = userEvent.setup()
    renderNewService()

    await screen.findByRole('heading', { name: 'New service' })
    await user.type(screen.getByLabelText('Name'), 'Test Validation')
    await user.click(screen.getByRole('button', { name: 'Save service' }))

    expect(await screen.findByText('Enter the price.')).toBeInTheDocument()
    expect(createService).not.toHaveBeenCalled()
  })

  it('shows each server field error beside its own input', async () => {
    const user = userEvent.setup()
    vi.mocked(createService).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: {
          categoryId: ['Choose a category for this service.'],
          eligibleStaffIds: [
            'An active service needs at least one active staff member who can perform it.',
          ],
        },
      }),
    )
    renderNewService()

    await screen.findByRole('heading', { name: 'New service' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save service' }))

    expect(
      await screen.findByText('Choose a category for this service.'),
    ).toBeInTheDocument()
    expect(
      await screen.findByText(
        'An active service needs at least one active staff member who can perform it.',
      ),
    ).toBeInTheDocument()
  })

  it('posts the draft and reports the save', async () => {
    const user = userEvent.setup()
    vi.mocked(createService).mockResolvedValue({} as Service)
    const onSaved = vi.fn()
    renderNewService(onSaved)

    await screen.findByRole('heading', { name: 'New service' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save service' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(createService).toHaveBeenCalledWith({
      name: 'Gel Manicure',
      categoryId: null,
      description: '',
      durationMinutes: 60,
      price: 28,
      isActive: true,
      isOnlineBookable: true,
      sortOrder: 0,
      eligibleStaffIds: [],
    })
  })

  it('shows a form-level banner when the failure carries no field errors', async () => {
    const user = userEvent.setup()
    vi.mocked(createService).mockRejectedValue(
      new ApiError(409, { status: 409, detail: 'A service with this name already exists.' }),
    )
    renderNewService()

    await screen.findByRole('heading', { name: 'New service' })
    await fillRequiredFields(user)
    await user.click(screen.getByRole('button', { name: 'Save service' }))

    expect(
      await screen.findByText('A service with this name already exists.'),
    ).toBeInTheDocument()
  })
})
