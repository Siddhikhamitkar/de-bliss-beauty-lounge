import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client.ts'
import {
  createBundle,
  getBundle,
  listCategories,
  searchIndividualServices,
  updateBundle,
} from '../api/catalogue.ts'
import type { Bundle, PagedResult, ServiceCategory, ServiceSummary } from '../api/contracts.ts'
import { formatPrice } from '../format.ts'
import { BundleEditor } from './BundleForm.tsx'

vi.mock('../api/catalogue.ts', () => ({
  createBundle: vi.fn(),
  getBundle: vi.fn(),
  listCategories: vi.fn(),
  searchIndividualServices: vi.fn(),
  updateBundle: vi.fn(),
}))

const hair: ServiceCategory = {
  id: 'cat1',
  name: 'Hair',
  description: null,
  sortOrder: 0,
  isActive: true,
  serviceCount: 2,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: null,
}

function individual(id: string, name: string, durationMinutes: number, price: number): ServiceSummary {
  return {
    id,
    name,
    categoryId: 'cat1',
    categoryName: 'Hair',
    durationMinutes,
    price,
    isActive: true,
    isOnlineBookable: true,
    isBookable: true,
    eligibleStaffCount: 1,
    sortOrder: 0,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: null,
    itemKind: 'Service',
    serviceCount: null,
    standardTotalPrice: null,
  }
}

// The cut is 45 minutes at 40, the colour an hour at 80: together a 105-minute
// offering that costs 120 bought separately.
const cut = individual('s1', 'Cut', 45, 40)
const colour = individual('s2', 'Colour', 60, 80)

const existing: Bundle = {
  id: 'b1',
  name: 'Makeover',
  description: 'Colour, then the cut.',
  categoryId: 'cat1',
  categoryName: 'Hair',
  isCategoryActive: true,
  durationMinutes: 105,
  price: 100,
  standardTotalPrice: 120,
  savingsAmount: 20,
  isActive: true,
  isBookable: true,
  sortOrder: 3,
  // The scalp ritual has since gone inactive, so the picker no longer offers it.
  items: [
    { serviceId: 's2', name: 'Colour', durationMinutes: 60, price: 80, displayOrder: 0, isActive: true },
    { serviceId: 's9', name: 'Scalp Ritual', durationMinutes: 15, price: 30, displayOrder: 1, isActive: false },
  ],
  createdAt: '2026-09-02T00:00:00Z',
  updatedAt: null,
}

function page<T>(items: T[]): PagedResult<T> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 200,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  }
}

/** The chosen components, top to bottom — the order they will be delivered in. */
function chosenComponents(): HTMLElement[] {
  return screen.getAllByRole('listitem')
}

/** One row of the "What the customer gets" totals. */
function summary(label: string): HTMLElement {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling as HTMLElement
}

async function pickBoth(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('checkbox', { name: /Colour/ }))
  await user.click(screen.getByRole('checkbox', { name: /Cut/ }))
}

function renderNewBundle(onSaved: () => void = () => {}) {
  return render(<BundleEditor bundleId={null} onSaved={onSaved} onCancel={() => {}} />)
}

describe('BundleEditor', () => {
  beforeEach(() => {
    vi.mocked(listCategories).mockResolvedValue([hair])
    vi.mocked(searchIndividualServices).mockResolvedValue(page([cut, colour]))
    vi.mocked(getBundle).mockResolvedValue(existing)
    vi.mocked(createBundle).mockResolvedValue(existing)
    vi.mocked(updateBundle).mockResolvedValue(existing)
  })

  it('offers the active individual services a bundle can be made of', async () => {
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })

    // Only active services can be added, and a bundle is never a component of
    // another bundle, so the picker reads the individual-service list.
    await waitFor(() =>
      expect(searchIndividualServices).toHaveBeenCalledWith({ status: 'Active', pageSize: 200 }),
    )
    expect(screen.getByText(/Pick at least 2 services below to start a bundle./)).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /Colour/ })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /Cut/ })).toBeInTheDocument()
  })

  it('totals the bundle from the components chosen', async () => {
    const user = userEvent.setup()
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    await pickBoth(user)

    expect(summary('Services')).toHaveTextContent('2')
    expect(summary('Duration')).toHaveTextContent('1 hr 45 min')
    expect(summary('Bought separately')).toHaveTextContent(formatPrice(120))
    // No price yet, so there is no saving to show.
    expect(summary('Saving')).toHaveTextContent('—')

    await user.type(screen.getByLabelText('Bundle price'), '100')

    expect(summary('Saving')).toHaveTextContent(formatPrice(20))
  })

  it('warns when the bundle costs more than its services bought separately', async () => {
    const user = userEvent.setup()
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    await pickBoth(user)
    await user.type(screen.getByLabelText('Bundle price'), '121')

    expect(
      await screen.findByRole('alert'),
    ).toHaveTextContent('A bundle cannot cost more than its services bought separately.')
  })

  it('reorders the delivery sequence and refuses to move past either end', async () => {
    const user = userEvent.setup()
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    await pickBoth(user)

    // Colour was picked first, so it is delivered first.
    expect(
      within(chosenComponents()[0]).getByText('Colour'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move Colour earlier' })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Move Colour later' }))

    const [first, second] = chosenComponents()
    expect(within(first).getByText('Cut')).toBeInTheDocument()
    expect(within(second).getByText('Colour')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move Colour later' })).toBeDisabled()
  })

  it('puts a removed component back on offer', async () => {
    const user = userEvent.setup()
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    await pickBoth(user)
    await user.click(screen.getByRole('button', { name: 'Remove Cut from the bundle' }))

    expect(chosenComponents()).toHaveLength(1)
    expect(summary('Services')).toHaveTextContent('1')
    expect(screen.getByRole('checkbox', { name: /Cut/ })).toBeInTheDocument()
  })

  it('blocks the save and marks the price field when it is blank', async () => {
    const user = userEvent.setup()
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    await user.type(screen.getByLabelText('Name'), 'Makeover')
    await user.click(screen.getByRole('button', { name: 'Save bundle' }))

    expect(await screen.findByText('Enter the price.')).toBeInTheDocument()
    expect(createBundle).not.toHaveBeenCalled()
  })

  it('posts the whole component set in the chosen delivery order', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    renderNewBundle(onSaved)

    await screen.findByRole('heading', { name: 'New bundle' })
    await user.type(screen.getByLabelText('Name'), 'Makeover')
    await user.selectOptions(screen.getByLabelText('Category'), 'cat1')
    await user.type(screen.getByLabelText('Description'), 'Colour, then the cut.')
    await user.type(screen.getByLabelText('Bundle price'), '100')
    await user.type(screen.getByLabelText('Position'), '3')
    await pickBoth(user)
    await user.click(screen.getByRole('button', { name: 'Save bundle' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(createBundle).toHaveBeenCalledWith({
      name: 'Makeover',
      categoryId: 'cat1',
      description: 'Colour, then the cut.',
      price: 100,
      isActive: true,
      sortOrder: 3,
      items: ['s2', 's1'],
    })
  })

  it('shows each server field error beside its own input', async () => {
    const user = userEvent.setup()
    vi.mocked(createBundle).mockRejectedValue(
      new ApiError(400, {
        status: 400,
        errors: {
          name: ["A bundle called 'Makeover' already exists in Hair."],
          price: ['A bundle cannot cost more than 120.00, its services bought separately.'],
        },
      }),
    )
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    // Held on to before the save: once a message lands under an input the label
    // no longer reads as just its own caption.
    const nameField = screen.getByLabelText('Name').parentElement as HTMLElement
    const priceField = screen.getByLabelText('Bundle price').parentElement as HTMLElement
    await user.type(screen.getByLabelText('Name'), 'Makeover')
    await user.selectOptions(screen.getByLabelText('Category'), 'cat1')
    await user.type(screen.getByLabelText('Bundle price'), '130')
    await pickBoth(user)
    await user.click(screen.getByRole('button', { name: 'Save bundle' }))

    expect(
      await within(nameField).findByText("A bundle called 'Makeover' already exists in Hair."),
    ).toBeInTheDocument()
    expect(
      within(priceField).getByText(
        'A bundle cannot cost more than 120.00, its services bought separately.',
      ),
    ).toBeInTheDocument()
  })

  it('shows a form-level banner when the failure carries no field errors', async () => {
    const user = userEvent.setup()
    vi.mocked(createBundle).mockRejectedValue(
      new ApiError(409, { status: 409, detail: "A bundle called 'Makeover' already exists." }),
    )
    renderNewBundle()

    await screen.findByRole('heading', { name: 'New bundle' })
    await user.type(screen.getByLabelText('Name'), 'Makeover')
    await user.type(screen.getByLabelText('Bundle price'), '100')
    await pickBoth(user)
    await user.click(screen.getByRole('button', { name: 'Save bundle' }))

    expect(
      await screen.findByText("A bundle called 'Makeover' already exists."),
    ).toBeInTheDocument()
  })

  it('edits a bundle, keeping a component the picker no longer offers', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<BundleEditor bundleId="b1" onSaved={onSaved} onCancel={() => {}} />)

    await screen.findByRole('heading', { name: 'Edit Makeover' })

    expect(getBundle).toHaveBeenCalledWith('b1')
    expect(screen.getByLabelText('Name')).toHaveValue('Makeover')
    expect(screen.getByLabelText('Category')).toHaveValue('cat1')
    expect(screen.getByLabelText('Bundle price')).toHaveValue(100)
    expect(screen.getByLabelText('Position')).toHaveValue(3)

    // Hiding the retired component would let a save silently drop it.
    const [first, second] = chosenComponents()
    expect(within(first).getByText('Colour')).toBeInTheDocument()
    expect(within(second).getByText('Scalp Ritual')).toBeInTheDocument()
    expect(within(second).getByText('inactive')).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: /Scalp Ritual/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save bundle' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce())
    expect(updateBundle).toHaveBeenCalledWith('b1', {
      name: 'Makeover',
      categoryId: 'cat1',
      description: 'Colour, then the cut.',
      price: 100,
      isActive: true,
      sortOrder: 3,
      items: ['s2', 's9'],
    })
    expect(createBundle).not.toHaveBeenCalled()
  })

  it('shows the load failure with a way back to the list', async () => {
    const user = userEvent.setup()
    vi.mocked(getBundle).mockRejectedValue(new Error('boom'))
    const onCancel = vi.fn()
    render(<BundleEditor bundleId="b1" onSaved={() => {}} onCancel={onCancel} />)

    expect(await screen.findByText('boom')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Back to services' }))

    expect(onCancel).toHaveBeenCalledOnce()
  })
})
