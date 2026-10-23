using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Imports;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Imports;

namespace DeBliss.UnitTests.Imports;

// Plain objects only: no context, provider, database, seed, or application host.
internal sealed class ServiceImportFixture
{
    public ServiceListRepository Services { get; } = new();
    public CategoryListRepository Categories { get; } = new();
    public StaffListDirectory Staff { get; } = new();
    public ListUnitOfWork UnitOfWork { get; }
    public ServiceCatalogueService Catalogue { get; }
    public ServiceImportService Imports { get; }
    public ServiceCategory Hair { get; } = new() { Id = Guid.NewGuid(), Name = "Hair" };
    public Staff Ada { get; } = new() { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };
    public Staff Grace { get; } = new() { Id = Guid.NewGuid(), FirstName = "Grace", LastName = "Hopper" };
    public Staff Inactive { get; } = new() { Id = Guid.NewGuid(), FirstName = "Inactive", LastName = "Stylist", IsActive = false };

    public ServiceImportFixture()
    {
        Categories.Items.Add(Hair);
        Staff.Items.AddRange([Ada, Grace, Inactive]);
        UnitOfWork = new ListUnitOfWork(Services);
        Catalogue = new ServiceCatalogueService(Services, new UnusedBundleRepository(), Categories, Staff, UnitOfWork);
        Imports = new ServiceImportService(new ClosedXmlImportFiles(), Categories, Staff, Catalogue, UnitOfWork);
    }

    public Service Existing(string name = "Cut", ServiceCategory? category = null)
    {
        category ??= Hair;
        var service = new Service
        {
            Id = Guid.NewGuid(), Name = name, ServiceCategoryId = category.Id, ServiceCategory = category,
            Price = 77m, DurationMinutes = 25, Description = "Keep this description",
            IsActive = false, IsOnlineBookable = true, SortOrder = 9
        };
        service.StaffServices.Add(new StaffService { Staff = Grace, StaffId = Grace.Id, Service = service, ServiceId = service.Id });
        Services.Items.Add(service);
        return service;
    }

    public void AssertNoWrites()
    {
        Assert.Equal(0, Services.AddCalls);
        Assert.Empty(Services.Pending);
        Assert.Equal(0, Categories.AddCalls);
        Assert.Equal(0, UnitOfWork.SaveCalls);
        Assert.Equal(0, UnitOfWork.BeginCalls);
    }
}

internal sealed class ServiceListRepository : IServiceRepository
{
    public List<Service> Items { get; } = [];
    public List<Service> Pending { get; } = [];
    public int AddCalls { get; private set; }

    public void Add(Service service)
    {
        AddCalls++;
        service.Id = Guid.NewGuid();
        foreach (var assignment in service.StaffServices)
        {
            assignment.Id = Guid.NewGuid();
            assignment.ServiceId = service.Id;
            assignment.StaffId = assignment.Staff.Id;
        }
        Pending.Add(service);
    }

    public Task<bool> ExistsNameInCategoryAsync(string name, Guid categoryId, Guid? excludingId, CancellationToken cancellationToken = default) =>
        Result(Items.Any(item => item.Id != excludingId && item.ServiceCategoryId == categoryId
            && string.Equals(item.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)), cancellationToken);

    public Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Result(Items.SingleOrDefault(item => item.Id == id), cancellationToken);

    public Task<IReadOnlyList<Service>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<Service>>(Items.Where(item => ids.Contains(item.Id)).ToArray(), cancellationToken);

    // Import must use catalogue validation, not a paged menu to guess uniqueness.
    public Task<IReadOnlyList<Service>> SearchAsync(ServiceQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Service>> GetBookableAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Service>> GetPublicAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    internal static Task<T> Result<T>(T result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }
}

internal sealed class CategoryListRepository : IServiceCategoryRepository
{
    public List<ServiceCategory> Items { get; } = [];
    public int AddCalls { get; private set; }

    public Task<ServiceCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceListRepository.Result(Items.SingleOrDefault(item => item.Id == id), cancellationToken);

    public Task<IReadOnlyList<ServiceCategory>> QueryAsync(ServiceStatusFilter status, CancellationToken cancellationToken = default) =>
        ServiceListRepository.Result<IReadOnlyList<ServiceCategory>>(Items.Where(item => status == ServiceStatusFilter.All
            || item.IsActive == (status == ServiceStatusFilter.Active)).ToArray(), cancellationToken);

    public Task<bool> ExistsNameAsync(string name, Guid? excludingId, CancellationToken cancellationToken = default) =>
        ServiceListRepository.Result(Items.Any(item => item.Id != excludingId
            && string.Equals(item.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)), cancellationToken);

    public Task<IReadOnlyDictionary<Guid, int>> CountServicesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Add(ServiceCategory category)
    {
        AddCalls++;
        throw new InvalidOperationException("Service imports must never create categories.");
    }
}

internal sealed class StaffListDirectory : IStaffDirectory
{
    public List<Staff> Items { get; } = [];
    public Action? BeforeRead { get; set; }

    public Task<IReadOnlyList<Staff>> GetSelectableAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke();
        return ServiceListRepository.Result<IReadOnlyList<Staff>>(Items.Where(item => includeInactive || item.IsActive).ToArray(), cancellationToken);
    }

    public Task<IReadOnlyList<Staff>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ServiceListRepository.Result<IReadOnlyList<Staff>>(Items.Where(item => ids.Contains(item.Id)).ToArray(), cancellationToken);
}

internal sealed class UnusedBundleRepository : IServiceBundleRepository
{
    public Task<ServiceBundle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ServiceBundle>> SearchAsync(ServiceQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ServiceBundle>> GetBookableAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> ExistsNameInCategoryAsync(string name, Guid categoryId, Guid? excludingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void Add(ServiceBundle bundle) => throw new NotSupportedException();
}

// Save flushes pending objects into the visible list. Disposing an uncommitted
// transaction restores the pre-transaction list, including after an earlier Save.
internal sealed class ListUnitOfWork(ServiceListRepository services) : IUnitOfWork
{
    public const string PrivateFailure = "Internal connection detail: do not expose";
    public int SaveCalls { get; private set; }
    public int SuccessfulSaves { get; private set; }
    public int BeginCalls { get; private set; }
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }
    public int? FailOnSave { get; set; }
    public Action? OnBegin { get; set; }
    public Action? AfterSave { get; set; }
    private ServiceListRepository Repository => services;
    private bool transactionOpen;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.True(transactionOpen, "Import writes must be enclosed in a transaction.");
        SaveCalls++;
        if (SaveCalls == FailOnSave)
            throw new InvalidOperationException(PrivateFailure);

        var count = services.Pending.Count;
        services.Items.AddRange(services.Pending);
        services.Pending.Clear();
        SuccessfulSaves++;
        AfterSave?.Invoke();
        return Task.FromResult(count);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.False(transactionOpen);
        BeginCalls++;
        // A concurrent writer lands before the transaction snapshot, so rollback
        // must preserve its row rather than treating it as one of our writes.
        OnBegin?.Invoke();
        transactionOpen = true;
        return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this, services.Items.ToArray()));
    }

    private sealed class Transaction(ListUnitOfWork owner, Service[] snapshot) : IUnitOfWorkTransaction
    {
        private bool committed;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Empty(owner.Repository.Pending);
            committed = true;
            owner.Commits++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!committed)
            {
                owner.Repository.Items.Clear();
                owner.Repository.Items.AddRange(snapshot);
                owner.Repository.Pending.Clear();
                owner.Rollbacks++;
            }
            owner.transactionOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
