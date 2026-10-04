import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from './DashboardPage.tsx'
import type { ReceptionistDashboard } from '../api/contracts.ts'
import { getReceptionistDashboard } from '../api/dashboard.ts'

vi.mock('../api/dashboard.ts', () => ({
  getReceptionistDashboard: vi.fn(),
}))

const getDashboard = vi.mocked(getReceptionistDashboard)

const FULL: ReceptionistDashboard = {
  generatedAt: '2026-10-03T10:00:00+04:00',
  today: {
    date: '2026-10-03',
    total: 5,
    completed: 1,
    inProgress: 1,
    upcoming: 2,
    noShow: 1,
    cancelled: 0,
    walkIns: 1,
  },
  schedule: [
    {
      id: 'done-1',
      customerId: 'cust-1',
      customerName: 'Sara Haddad',
      startsAt: '2026-10-03T08:00:00+04:00',
      endsAt: '2026-10-03T08:30:00+04:00',
      status: 'Completed',
      servicesLabel: 'Haircut',
      staffName: 'Jasmin',
      staffColor: '#c9a86a',
      canCheckout: true,
    },
    {
      id: 'live-2',
      customerId: null,
      customerName: 'Walk-in',
      startsAt: '2026-10-03T09:00:00+04:00',
      endsAt: '2026-10-03T09:45:00+04:00',
      status: 'InProgress',
      servicesLabel: 'Colour',
      staffName: null,
      staffColor: null,
      canCheckout: false,
    },
  ],
  upcoming: [
    {
      id: 'next-3',
      customerId: 'cust-2',
      customerName: 'Mona Nader',
      startsAt: '2026-10-03T11:00:00+04:00',
      endsAt: '2026-10-03T11:30:00+04:00',
      status: 'Confirmed',
      servicesLabel: 'Blow-dry',
      staffName: 'Nadia',
      staffColor: null,
      canCheckout: false,
    },
  ],
  staff: [
    {
      id: 'staff-1',
      firstName: 'Jasmin',
      lastName: 'Rae',
      jobTitle: 'Senior stylist',
      color: '#c9a86a',
      status: 'Available',
      statusNote: null,
    },
    {
      id: 'staff-2',
      firstName: 'Mira',
      lastName: 'Slim',
      jobTitle: null,
      color: null,
      status: 'WithClient',
      statusNote: null,
    },
  ],
  alerts: {
    birthdaysToday: 1,
    birthdayNamesToday: ['Sara Haddad'],
    birthdayNamesLaterThisWeek: ['Mona Nader', 'Lulu Farah'],
    firstVisitsToday: 1,
    recentNotes: [
      {
        customerId: 'cust-1',
        customerName: 'Sara Haddad',
        note: 'Prefers the window chair',
        createdAt: '2026-10-03T09:40:00+04:00',
      },
    ],
  },
  popularServices: [
    { name: 'Haircut', count: 3 },
    { name: 'Colour', count: 2 },
  ],
}

const EMPTY: ReceptionistDashboard = {
  generatedAt: '2026-10-03T10:00:00+04:00',
  today: {
    date: '2026-10-03',
    total: 0,
    completed: 0,
    inProgress: 0,
    upcoming: 0,
    noShow: 0,
    cancelled: 0,
    walkIns: 0,
  },
  schedule: [],
  upcoming: [],
  staff: [],
  alerts: {
    birthdaysToday: 0,
    birthdayNamesToday: [],
    birthdayNamesLaterThisWeek: [],
    firstVisitsToday: 0,
    recentNotes: [],
  },
  popularServices: [],
}

function renderPage(overrides: Partial<Parameters<typeof DashboardPage>[0]> = {}) {
  const props = {
    onOpenAppointment: vi.fn(),
    onNewAppointment: vi.fn(),
    onAddCustomer: vi.fn(),
    onFindCustomer: vi.fn(),
    onOpenBilling: vi.fn(),
    onCheckout: vi.fn(),
    onViewAllAppointments: vi.fn(),
    ...overrides,
  }
  render(<DashboardPage {...props} />)
  return props
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('shows the workload cards from the snapshot', async () => {
    getDashboard.mockResolvedValue(FULL)
    renderPage()

    for (const [label, value] of [
      ['Appointments', '5'],
      ['Upcoming', '2'],
      ['In progress', '1'],
      ['Completed', '1'],
      ['No-shows', '1'],
      ['Walk-ins', '1'],
    ] as const) {
      const card = (await screen.findAllByText(label))
        .find(el => el.className === 'dash-card-label')
        ?.closest('.dash-card')
      expect(card, `card ${label}`).not.toBeNull()
      expect(card).toHaveTextContent(value)
    }
  })

  it('greets with the daypart, brand subline and today’s date', async () => {
    getDashboard.mockResolvedValue(FULL)
    renderPage()

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: /Good (morning|afternoon|evening)/,
      }),
    ).toBeInTheDocument()
    expect(screen.getByText(/happening at De Bliss today/)).toBeInTheDocument()
    expect(document.querySelector('.dash-hero-eyebrow')?.textContent).toMatch(/2026/)
  })

  it('lists the schedule and opens an appointment on click', async () => {
    const user = userEvent.setup()
    getDashboard.mockResolvedValue(FULL)
    const props = renderPage()

    const row = await screen.findByRole('button', { name: /Sara Haddad/ })
    expect(row).toHaveTextContent('Haircut · Jasmin')

    await user.click(row)
    expect(props.onOpenAppointment).toHaveBeenCalledWith('done-1')
  })

  it('offers checkout only on completed visits and routes it to the app', async () => {
    const user = userEvent.setup()
    getDashboard.mockResolvedValue(FULL)
    const props = renderPage()

    const checkout = await screen.findByRole('button', { name: 'Checkout' })
    await user.click(checkout)
    expect(props.onCheckout).toHaveBeenCalledWith('done-1')

    // Only one checkout button: the in-progress walk-in has none.
    expect(screen.getAllByRole('button', { name: 'Checkout' })).toHaveLength(1)
  })

  it('fires every quick action and the view-all shortcut', async () => {
    const user = userEvent.setup()
    getDashboard.mockResolvedValue(FULL)
    const props = renderPage()

    await user.click(await screen.findByRole('button', { name: /^New appointment/ }))
    expect(props.onNewAppointment).toHaveBeenCalledOnce()

    await user.click(screen.getByRole('button', { name: /^Add customer/ }))
    expect(props.onAddCustomer).toHaveBeenCalledOnce()

    await user.click(screen.getByRole('button', { name: /^Find customer/ }))
    expect(props.onFindCustomer).toHaveBeenCalledOnce()

    await user.click(screen.getByRole('button', { name: /^Billing & checkout/ }))
    expect(props.onOpenBilling).toHaveBeenCalledOnce()

    await user.click(screen.getByRole('button', { name: /View all appointments/ }))
    expect(props.onViewAllAppointments).toHaveBeenCalledOnce()
  })

  it('shows staff statuses with friendly labels', async () => {
    getDashboard.mockResolvedValue(FULL)
    renderPage()

    expect(await screen.findByText('Jasmin Rae')).toBeInTheDocument()
    expect(screen.getByText('Available')).toBeInTheDocument()
    expect(screen.getByText('Mira Slim')).toBeInTheDocument()
    expect(screen.getByText('With client')).toBeInTheDocument()
  })

  it('renders birthday, first-visit and note alerts from real data', async () => {
    getDashboard.mockResolvedValue(FULL)
    renderPage()

    expect(await screen.findByText(/Wish a happy birthday to Sara Haddad\./)).toBeInTheDocument()
    expect(screen.getByText(/1 first-time customer today/)).toBeInTheDocument()
    expect(screen.getByText('Prefers the window chair')).toBeInTheDocument()
    expect(screen.getByText(/Sara Haddad · /)).toBeInTheDocument()
    expect(screen.getByText('Popular today')).toBeInTheDocument()
    expect(screen.getByText('×3')).toBeInTheDocument()
  })

  it('falls back to later-this-week birthdays when none are today', async () => {
    getDashboard.mockResolvedValue({
      ...FULL,
      alerts: {
        ...FULL.alerts,
        birthdaysToday: 0,
        birthdayNamesToday: [],
      },
    })
    renderPage()

    expect(
      await screen.findByText(/No birthdays today\. Coming up this week: Mona Nader and Lulu Farah\./),
    ).toBeInTheDocument()
  })

  it('renders premium empty states when nothing is happening', async () => {
    getDashboard.mockResolvedValue(EMPTY)
    renderPage()

    expect(await screen.findByText('No appointments on the books yet today.')).toBeInTheDocument()
    expect(screen.getByText('Nothing else booked after right now — the rest of today is clear.')).toBeInTheDocument()
    expect(screen.getByText('No active staff yet — add the team in the Staff section.')).toBeInTheDocument()
    expect(screen.getByText('No birthdays in the next seven days.')).toBeInTheDocument()
    expect(screen.getByText('No first-time customers today.')).toBeInTheDocument()
    expect(screen.getByText('No reception notes yet.')).toBeInTheDocument()
    // With no workload the popular-services panel hides entirely.
    expect(screen.queryByText('Popular today')).not.toBeInTheDocument()
  })

  it('shows a retryable error when the snapshot fails', async () => {
    getDashboard.mockRejectedValue(new Error('offline'))
    renderPage()

    expect(await screen.findByText('offline')).toBeInTheDocument()

    getDashboard.mockResolvedValue(FULL)
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Appointments')).toBeInTheDocument()
  })
})
