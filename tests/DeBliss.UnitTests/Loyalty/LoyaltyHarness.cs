using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers.Dtos;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Loyalty;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Application.Loyalty.Ports;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Loyalty;
using DeBliss.Domain.Scheduling;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Loyalty;

internal sealed class LoyaltyHarness
{
    public Guid CustomerId { get; } = Guid.NewGuid();
    public MemoryLoyaltyRepository Repository { get; } = new();
    public MemoryUnitOfWork UnitOfWork { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    public LoyaltyService Service { get; }

    public LoyaltyHarness()
    {
        Service = new LoyaltyService(Repository, new CustomerLookup(CustomerId), UnitOfWork, Clock);
    }

    public LoyaltySettings Enable(decimal balance = 100m)
    {
        Repository.Settings = new LoyaltySettings { IsEnabled = true };
        if (balance != 0m)
            Repository.Transactions.Add(new CustomerLoyaltyTransaction
            {
                CustomerId = CustomerId,
                Type = balance > 0m ? LoyaltyTransactionType.Earn : LoyaltyTransactionType.Reversal,
                Points = balance,
                PointValueAed = 1m,
                OccurredAt = Clock.GetUtcNow(),
                Description = "Existing points",
                IdempotencyKey = "test:existing"
            });
        return Repository.Settings;
    }

    public Task<LoyaltyCalculation> Calculate(
        decimal amount = 100m, decimal points = 0m, decimal tax = 5m, bool inclusive = false) =>
        Service.CalculateAsync(CustomerId, amount, tax, inclusive, points);

    internal sealed class MemoryLoyaltyRepository : ILoyaltyRepository
    {
        public LoyaltySettings? Settings { get; set; }
        public List<CustomerLoyaltyTransaction> Transactions { get; } = [];
        public int SettingsAdded { get; private set; }
        public bool TrackedSettingsRequested { get; private set; }

        public Task<LoyaltySettings?> GetSettingsAsync(bool tracked = false, CancellationToken ct = default)
        {
            TrackedSettingsRequested |= tracked;
            return Task.FromResult(Settings);
        }

        public void AddSettings(LoyaltySettings settings)
        {
            SettingsAdded++;
            Settings = settings;
        }

        public Task<LoyaltyTotals> GetTotalsAsync(Guid customerId, CancellationToken ct = default)
        {
            var entries = Transactions.Where(entry => entry.CustomerId == customerId).ToList();
            return Task.FromResult(new LoyaltyTotals(entries.Sum(entry => entry.Points),
                entries.Where(entry => entry.Type == LoyaltyTransactionType.Earn).Sum(entry => entry.Points),
                -entries.Where(entry => entry.Type == LoyaltyTransactionType.Redeem).Sum(entry => entry.Points)));
        }

        public Task<IReadOnlyList<LoyaltyTransactionDto>> GetRecentTransactionsAsync(
            Guid customerId, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LoyaltyTransactionDto>>(History(customerId).Take(count).ToList());

        public Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
            Guid customerId, int page, int pageSize, CancellationToken ct = default)
        {
            var entries = History(customerId).ToList();
            return Task.FromResult<(IReadOnlyList<LoyaltyTransactionDto>, int)>(
                (entries.Skip((page - 1) * pageSize).Take(pageSize).ToList(), entries.Count));
        }

        public void AddTransaction(CustomerLoyaltyTransaction transaction) => Transactions.Add(transaction);

        private IEnumerable<LoyaltyTransactionDto> History(Guid customerId) => Transactions
            .Where(entry => entry.CustomerId == customerId)
            .OrderByDescending(entry => entry.OccurredAt).ThenByDescending(entry => entry.Id)
            .Select(entry => new LoyaltyTransactionDto(entry.Id, entry.Type.ToString(), entry.CustomerId,
                entry.InvoiceId, null, entry.Points, entry.OccurredAt, entry.Description, entry.EligibleSpend,
                entry.EarningPercentage, entry.PointValueAed, entry.CreatedByUserId,
                entry.IdempotencyKey, entry.ReversesTransactionId));
    }

    internal sealed class MemoryUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(++Saves);
        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Checkout owns the transaction.");
    }

    private sealed class CustomerLookup(Guid customerId) : ICustomerRepository
    {
        public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == customerId ? new Customer { Id = id } : null);
        public Task<Customer?> FindLinkableByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Customer> Items, int TotalCount)> SearchAsync(CustomerQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Customer>> GetByNormalizedPhoneAsync(string normalizedPhone, Guid? excludeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CustomerNote?> GetNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerNote>> ListNotesAsync(Guid customerId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(Guid customerId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(int Total, int Completed, int Cancelled, int NoShow, DateTimeOffset? LastVisitAt)> GetAppointmentSummaryAsync(Guid customerId, CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(Customer customer) => throw new NotSupportedException();
        public void AddNote(CustomerNote note) => throw new NotSupportedException();
    }
}
