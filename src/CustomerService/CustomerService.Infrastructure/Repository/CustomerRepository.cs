using Collaboration.Domain.Repository;
using CustomerService.Domain.Entities;
using CustomerService.Domain.IRepository;
using FreeSql;

namespace CustomerService.Infrastructure.Repository;

/// <summary>客户账号仓储实现。软删与可见性条件由 AOP 注入，这里不重复写。</summary>
public sealed class CustomerRepository : CrudRepository<Customer>, ICustomerRepository
{
    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册 AOP 的 FreeSql 单例。</param>
    public CustomerRepository(IFreeSql freeSql) : base(freeSql)
    {
    }

    /// <inheritdoc />
    public async Task<Customer?> GetByNameAsync(
        long platformId, string customerName, CancellationToken ct = default)
        => await Db.Select<Customer>()
            .Where(a => a.PlatformId == platformId && a.CustomerName == customerName)
            .FirstAsync(ct);

    /// <inheritdoc />
    public async Task<Customer?> GetByPhoneAsync(
        long platformId, string phone, CancellationToken ct = default)
        => await Db.Select<Customer>()
            .Where(a => a.PlatformId == platformId && a.Phone == phone)
            .FirstAsync(ct);

    /// <inheritdoc />
    public Task<bool> ExistsByNameAsync(
        long platformId, string customerName, CancellationToken ct = default)
        => Db.Select<Customer>()
            .Where(a => a.PlatformId == platformId && a.CustomerName == customerName)
            .AnyAsync(ct);

    /// <inheritdoc />
    public Task<bool> ExistsByPhoneAsync(
        long platformId, string phone, CancellationToken ct = default)
        => Db.Select<Customer>()
            .Where(a => a.PlatformId == platformId && a.Phone == phone)
            .AnyAsync(ct);

    /// <inheritdoc />
    public Task<int> UpdatePasswordAsync(
        long customerId, string passwordHash, CancellationToken ct = default)
        => Db.Update<Customer>()
            .Where(a => a.Id == customerId)
            .Set(a => new Customer
            {
                PasswordHash = passwordHash,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

    /// <inheritdoc />
    public async Task<(List<Customer> Items, long Total)> PageForAdminAsync(
        int status, long platformId, string keyword, int page, int pageSize,
        CancellationToken ct = default)
    {
        var select = Db.Select<Customer>()
            .Where(a => status <= 0 || a.Status == status)
            // 超管（platformId = 0）不限；平台账号由调用方传入本平台 Id（AOP 不覆盖客户表）
            .Where(a => platformId <= 0 || a.PlatformId == platformId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            select = select.Where(a => a.CustomerName.Contains(kw)
                                   || a.NickName.Contains(kw)
                                   || a.Phone.Contains(kw));
        }

        var total = await select.CountAsync(ct).ConfigureAwait(false);

        // 按注册时间倒序：新客户在前，运营最常关心的是「刚来的那批」。
        // 同一时刻注册的用 Id 兜底，避免顺序抖动导致翻页时重复或漏行。
        var items = await select
            .OrderByDescending(a => a.CreatedAt).OrderByDescending(a => a.Id)
            .Page(page, pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<int> TryChangeStatusAsync(
        long customerId, int expectedStatus, int newStatus, CancellationToken ct = default)
        => await Db.Update<Customer>()
            .Where(a => a.Id == customerId && a.Status == expectedStatus)
            .Set(a => new Customer { Status = newStatus, UpdatedAt = DateTime.UtcNow })
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<int> BackfillPlatformAsync(long platformId, CancellationToken ct = default)
        => await Db.Update<Customer>()
            // 只动「还没归属」的行：已经属于某个平台的账号不能被误改
            .Where(a => a.PlatformId == 0)
            .Set(a => new Customer { PlatformId = platformId, UpdatedAt = DateTime.UtcNow })
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);
}

