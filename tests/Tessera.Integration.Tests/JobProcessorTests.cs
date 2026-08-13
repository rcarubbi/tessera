using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Worker;
using Testcontainers.PostgreSql;

namespace Tessera.Integration.Tests;

// ExecuteUpdateAsync (the mechanism the atomic claim relies on) isn't supported by the EF Core InMemory
// provider, and SQLite can't order/compare DateTimeOffset columns, so these tests run against a real
// PostgreSQL container, matching production (Npgsql) behavior exactly.
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class JobProcessorTests : IClassFixture<PostgresContainerFixture>, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly TesseraDbContext _db;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public JobProcessorTests(PostgresContainerFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TesseraDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        _db.Database.EnsureCreated();

        // The container (and its schema) is shared across tests in this class; reset data between tests.
        _db.Repositories.RemoveRange(_db.Repositories);
        _db.SaveChanges();
    }

    private static Repository NewRepo(ProcessingStatus status, DateTimeOffset createdAt, DateTimeOffset? leaseExpiresAt = null, Guid? leaseId = null) => new()
    {
        Id = Guid.NewGuid(),
        FullName = $"acme/{Guid.NewGuid():N}",
        IsConnected = true,
        Status = status,
        ProcessingLeaseId = leaseId,
        LeaseExpiresAt = leaseExpiresAt,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private JobProcessor CreateProcessor() => new(_provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobProcessor>.Instance);

    [Fact]
    public async Task Claim_returns_pending_repository_and_sets_lease()
    {
        var repo = NewRepo(ProcessingStatus.Pending, DateTimeOffset.UtcNow);
        _db.Repositories.Add(repo);
        await _db.SaveChangesAsync();

        var claimed = await CreateProcessor().ClaimNextRepositoryAsync(_db, LeaseDuration, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(repo.Id, claimed!.Id);
        Assert.Equal(ProcessingStatus.Cloning, claimed.Status);
        Assert.NotNull(claimed.ProcessingLeaseId);
        Assert.NotNull(claimed.LeaseExpiresAt);
        Assert.True(claimed.LeaseExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Claim_returns_null_when_no_eligible_repository()
    {
        var claimed = await CreateProcessor().ClaimNextRepositoryAsync(_db, LeaseDuration, CancellationToken.None);

        Assert.Null(claimed);
    }

    [Fact]
    public async Task Two_processors_cannot_claim_the_same_pending_repository()
    {
        var repo = NewRepo(ProcessingStatus.Pending, DateTimeOffset.UtcNow);
        _db.Repositories.Add(repo);
        await _db.SaveChangesAsync();

        var processorA = CreateProcessor();
        var processorB = CreateProcessor();

        var claimedA = await processorA.ClaimNextRepositoryAsync(_db, LeaseDuration, CancellationToken.None);
        var claimedB = await processorB.ClaimNextRepositoryAsync(_db, LeaseDuration, CancellationToken.None);

        Assert.NotNull(claimedA);
        Assert.Null(claimedB);
    }

    [Fact]
    public async Task Live_lease_is_not_reclaimed()
    {
        var repo = NewRepo(
            ProcessingStatus.Analyzing,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            leaseExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            leaseId: Guid.NewGuid());
        _db.Repositories.Add(repo);
        await _db.SaveChangesAsync();

        var claimed = await CreateProcessor().ClaimNextRepositoryAsync(_db, LeaseDuration, CancellationToken.None);

        Assert.Null(claimed);
    }

    [Fact]
    public async Task Expired_lease_is_reclaimed_and_metadata_reset()
    {
        var originalLeaseId = Guid.NewGuid();
        var repo = NewRepo(
            ProcessingStatus.Analyzing,
            DateTimeOffset.UtcNow.AddMinutes(-30),
            leaseExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            leaseId: originalLeaseId);
        repo.ProcessedCount = 42;
        repo.TotalCount = 100;
        _db.Repositories.Add(repo);
        await _db.SaveChangesAsync();

        var claimed = await CreateProcessor().ClaimNextRepositoryAsync(_db, LeaseDuration, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(ProcessingStatus.Cloning, claimed!.Status);
        Assert.NotEqual(originalLeaseId, claimed.ProcessingLeaseId);
        Assert.True(claimed.LeaseExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(0, claimed.ProcessedCount);
        Assert.Equal(0, claimed.TotalCount);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
