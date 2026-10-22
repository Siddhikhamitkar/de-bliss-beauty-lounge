import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OwnerDashboardPage } from './OwnerDashboardPage.tsx'
import type { OwnerDashboard } from '../api/contracts.ts'
import { getOwnerDashboard } from '../api/dashboard.ts'

vi.mock('../api/dashboard.ts', () => ({
  getOwnerDashboard: vi.fn(),
}))

const getOwner = vi.mocked(getOwnerDashboard)

const FULL: OwnerDashboard = {
  generatedAt: '2026-10-03T10:00:00+04:00',
  timeZoneId: 'Asia/Dubai',
  kpis: {
    currency: 'AED',
    netRevenueToday: 230,
    netRevenueYesterday: 100,
    netRevenueMonthToDate: 330,
    netRevenueSamePeriodLastMonth: 400,
    completedAppointmentsToday: 2,
    upcomingAppointmentsToday: 3,
    averageTransactionValue: 115,
    paidInvoiceCount: 2,
    retailRevenue: 50,
    serviceRevenue: 200,
    outstandingAmount: 500,
    outstandingInvoiceCount: 2,
  },
  operations: {
    today: {
      date: '2026-10-03',
      total: 6,
      completed: 2,
      inProgress: 1,
      upcoming: 3,
      noShow: 0,
      cancelled: 0,
      walkIns: 1,
    },
    schedule: [
      {
        id: 'apt-1',
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
        id: 'apt-2',
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
    upcoming: [],
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
    ],
    awaitingCheckout: 1,
    conflicts: [
      {
        staffId: 'staff-1',
        staffName: 'Jasmin Rae',
        startsAt: '2026-10-03T17:00:00+04:00',
        endsAt: '2026-10-03T18:00:00+04:00',
        appointmentCount: 2,
      },
    ],
  },
  revenue: {
    rangeLabel: 'Today',
    from: '2026-10-03',
    to: '2026-10-03',
    netRevenue: 230,
    previousNetRevenue: 100,
    changePercent: 130,
    serviceRevenue: 200,
    retailRevenue: 50,
    vatTotal: 10,
    tipTotal: 15,
    discountTotal: 20,
    collected: 255,
    paidInvoiceCount: 2,
    averageTransactionValue: 115,
    daily: [
      { date: '2026-10-03', netRevenue: 230, completed: 2, cancelled: 0 },
    ],
    monthly: [
      { monthStart: '2026-05-01', netRevenue: 0 },
      { monthStart: '2026-06-01', netRevenue: 0 },
      { monthStart: '2026-07-01', netRevenue: 0 },
      { monthStart: '2026-08-01', netRevenue: 0 },
      { monthStart: '2026-09-01', netRevenue: 400 },
      { monthStart: '2026-10-01', netRevenue: 330 },
    ],
    topServices: [{ name: 'Haircut', revenue: 200, count: 1 }],
    categories: [{ name: 'Hair', revenue: 200, count: 1 }],
    retailProducts: [{ name: 'Shampoo', revenue: 50, count: 1 }],
  },
  inventory: {
    lowStockCount: 1,
    outOfStockCount: 1,
    items: [
      {
        productId: 'prod-1',
        name: 'Alpha Wax',
        currentStock: 0,
        minimumStock: 5,
        unitOfMeasure: 'pcs',
        status: 'OutOfStock',
      },
      {
        productId: 'prod-2',
        name: 'Beta Oil',
        currentStock: 3,
        minimumStock: 5,
        unitOfMeasure: 'pcs',
        status: 'LowStock',
      },
    ],
  },
  actions: [
    {
      severity: 'high',
      message: '1 schedule conflict today — two visits claim the same staff member.',
      count: 1,
      link: '/calendar',
    },
    {
      severity: 'medium',
      message: '2 bills left unpaid after checkout.',
      count: 2,
      link: '/billing',
    },
  ],
}

const EMPTY: OwnerDashboard = {
  generatedAt: '2026-10-03T10:00:00+04:00',
  timeZoneId: 'Asia/Dubai',
  kpis: {
    currency: 'AED',
    netRevenueToday: 0,
    netRevenueYesterday: 0,
    netRevenueMonthToDate: 0,
    netRevenueSamePeriodLastMonth: 0,
    completedAppointmentsToday: 0,
    upcomingAppointmentsToday: 0,
    averageTransactionValue: 0,
    paidInvoiceCount: 0,
    retailRevenue: 0,
    serviceRevenue: 0,
    outstandingAmount: 0,
    outstandingInvoiceCount: 0,
  },
  operations: {
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
    awaitingCheckout: 0,
    conflicts: [],
  },
  revenue: {
    rangeLabel: 'Today',
    from: '2026-10-03',
    to: '2026-10-03',
    netRevenue: 0,
    previousNetRevenue: 0,
    changePercent: null,
    serviceRevenue: 0,
    retailRevenue: 0,
    vatTotal: 0,
    tipTotal: 0,
    discountTotal: 0,
    collected: 0,
    paidInvoiceCount: 0,
    averageTransactionValue: 0,
    daily: [{ date: '2026-10-03', netRevenue: 0, completed: 0, cancelled: 0 }],
    monthly: [
      { monthStart: '2026-05-01', netRevenue: 0 },
      { monthStart: '2026-06-01', netRevenue: 0 },
      { monthStart: '2026-07-01', netRevenue: 0 },
      { monthStart: '2026-08-01', netRevenue: 0 },
      { monthStart: '2026-09-01', netRevenue: 0 },
      { monthStart: '2026-10-01', netRevenue: 0 },
    ],
    topServices: [],
    categories: [],
    retailProducts: [],
  },
  inventory: {
    lowStockCount: 0,
    outOfStockCount: 0,
    items: [],
  },
  actions: [],
}

function renderPage(overrides: Partial<Parameters<typeof OwnerDashboardPage>[0]> = {}) {
  return render(
    <OwnerDashboardPage
      firstName="Owner"
      onNavigate={vi.fn()}
      onOpenAppointment={vi.fn()}
      onCheckout={vi.fn()}
      {...overrides}
    />,
  )
}

describe('OwnerDashboardPage', () => {
  beforeEach(() => {
    getOwner.mockReset()
    getOwner.mockResolvedValue(FULL)
  })

  it('renders the money cards with the invoice currency', async () => {
    renderPage()

    // The range total repeats on the KPI card and the revenue panel.
    expect((await screen.findAllByText('AED 230.00')).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('AED 500.00')).toBeInTheDocument()
    // AED 50.00 repeats on the retail KPI card and the retail-products list.
    expect((await screen.findAllByText('AED 50.00')).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('vs yesterday AED 100.00')).toBeInTheDocument()
  })

  it('compares month to date with the same days of last month', async () => {
    renderPage()

    // 330 vs 400 is a 17.5% decline — shown, not hidden.
    expect(await screen.findByText('▼ 17.5%')).toBeInTheDocument()
    expect(screen.getByText('vs same days last month')).toBeInTheDocument()
  })

  it('flags the range change honestly when there is no prior period', async () => {
    getOwner.mockResolvedValue(EMPTY)
    renderPage()

    // The MTD card and the range headline both lack a comparison.
    expect((await screen.findAllByText('no prior period')).length).toBeGreaterThanOrEqual(1)
  })

  it('lists real attention items and routes their links', async () => {
    const onNavigate = vi.fn()
    renderPage({ onNavigate })

    expect(
      await screen.findByText('1 schedule conflict today — two visits claim the same staff member.'),
    ).toBeInTheDocument()

    const openBilling = screen.getAllByRole('button', { name: /open/i })[1]
    await userEvent.click(openBilling)

    expect(onNavigate).toHaveBeenCalledWith('/billing')
  })

  it('shows healthy-state copy when nothing needs attention', async () => {
    getOwner.mockResolvedValue(EMPTY)
    renderPage()

    expect(await screen.findByText('Nothing needs your attention right now.')).toBeInTheDocument()
    expect(screen.getByText('Stock levels are healthy.')).toBeInTheDocument()
  })

  it('passes the chosen range to the API', async () => {
    renderPage()

    await screen.findAllByText('AED 230.00')
    await userEvent.click(screen.getByRole('button', { name: 'This month' }))

    await waitFor(() => {
      expect(getOwner).toHaveBeenLastCalledWith('ThisMonth')
    })
  })

  it('custom dates travel with the request', async () => {
    renderPage()

    await screen.findAllByText('AED 230.00')
    await userEvent.click(screen.getByRole('button', { name: 'Custom' }))

    const from = await screen.findByLabelText('Custom range start')
    const to = screen.getByLabelText('Custom range end')

    // jsdom date inputs ignore typed keys; set them the way a real change fires.
    fireEvent.change(from, { target: { value: '2026-09-01' } })
    fireEvent.change(to, { target: { value: '2026-09-30' } })

    await waitFor(() => {
      expect(getOwner).toHaveBeenLastCalledWith('Custom', '2026-09-01', '2026-09-30')
    })
  })

  it('offers checkout for completed visits awaiting payment', async () => {
    const onCheckout = vi.fn()
    renderPage({ onCheckout })

    await screen.findByText('Sara Haddad')

    await userEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    expect(onCheckout).toHaveBeenCalledWith('apt-1')
  })

  it('reports schedule conflicts and stock alerts', async () => {
    renderPage()

    // The name sits inside a <strong>, so match the surrounding sentence.
    expect(await screen.findByText(/is double-booked/)).toBeInTheDocument()
    expect(screen.getByText('Alpha Wax')).toBeInTheDocument()
    expect(screen.getByText('Running low')).toBeInTheDocument()
  })

  it('shows the error state and retries', async () => {
    getOwner.mockRejectedValueOnce(new Error('boom'))
    getOwner.mockResolvedValue(FULL)
    renderPage()

    expect(await screen.findByText(/boom/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect((await screen.findAllByText('AED 230.00')).length).toBeGreaterThanOrEqual(1)
  })
})
