using Collaboration.Domain.Repository;
using FreeSql;
using PermissionService.Domain.Entities;
using PermissionService.Domain.IRepository;

namespace PermissionService.Infrastructure.Repository;

/// <summary>
/// 角色仓储实现，含角色-权限与账号-角色绑定的读写。
/// </summary>
/// <remarks>
/// InsertAsync / UpdateAsync / DeleteAsync 继承 CrudRepository——基类负责雪花 Id 与审计时间戳，
/// 不要重复实现，否则会绕过 Id 填充（Id 恒为 0 会连带破坏唯一性校验）。
/// </remarks>
public sealed class RoleRepository : CrudRepository<Role>, IRoleRepository
{
    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 单例。</param>
    public RoleRepository(IFreeSql freeSql) : base(freeSql)
    {
    }

    /// <inheritdoc />
    public async Task<(List<Role> Items, long Total)> QueryPagedAsync(int page, int pageSize, string keyword, CancellationToken ct = default)
    {
        var select = Db.Select<Role>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            select = select.Where(a => a.RoleName.Contains(kw) || a.Code.Contains(kw));
        }

        var total = await select.CountAsync(ct);
        var items = await select.OrderByDescending(a => a.Id).Page(page, pageSize).ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<List<Role>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new List<Role>();
        var list = ids.Distinct().ToArray();
        return await Db.Select<Role>().Where(a => list.Contains(a.Id)).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await Db.Select<Role>().Where(a => a.Code == code).FirstAsync(ct);

    /// <inheritdoc />
    public async Task<bool> ExistsByNameAsync(string roleName, CancellationToken ct = default)
        => await Db.Select<Role>().Where(a => a.RoleName == roleName).AnyAsync(ct);

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
        => await Db.Select<Role>().Where(a => a.Code == code).AnyAsync(ct);

    /// <inheritdoc />
    public async Task<List<long>> GetPermissionIdsAsync(long roleId, CancellationToken ct = default)
    {
        var rows = await Db.Select<RolePermission>().Where(a => a.RoleId == roleId).ToListAsync(ct);
        return rows.Select(x => x.PermissionId).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, int>> CountPermissionsByRoleAsync(
        IReadOnlyCollection<long> roleIds, CancellationToken ct = default)
    {
        if (roleIds.Count == 0) return new Dictionary<long, int>();

        var ids = roleIds.Distinct().ToArray();

        // 一次取回这些角色的绑定行、在内存里分组计数：逐角色查会让一页 20 个角色变成 20 条查询。
        // 只选 RoleId 一列（与 MerchantRepository.CountGroupByPlatformAsync 同一手法）：
        // FreeSql 的 GroupBy 翻译在本项目上不可靠，拉整行再分组在几十条数据量级上更省心。
        var rows = await Db.Select<RolePermission>()
            .Where(a => ids.Contains(a.RoleId))
            .ToListAsync(a => a.RoleId, ct);

        return rows.GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <inheritdoc />
    public Task<int> ReplaceRolePermissionsAsync(long roleId, IReadOnlyCollection<long> permissionIds, CancellationToken ct = default)
    {
        var ids = permissionIds.Distinct().ToArray();
        var affected = 0;

        Db.Transaction(() =>
        {
            affected += Db.Delete<RolePermission>().Where(a => a.RoleId == roleId).ExecuteAffrows();
            if (ids.Length == 0) return;

            var now = DateTime.UtcNow;
            affected += Db.Insert(new List<RolePermission>(ids.Select(pid => new RolePermission
            {
                RoleId = roleId,
                PermissionId = pid,
                CreatedAt = now
            }))).ExecuteAffrows();
        });

        return Task.FromResult(affected);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ResolvePermissionCodesAsync(long userId, CancellationToken ct = default)
    {
        var bindings = await Db.Select<UserRole>().Where(a => a.UserId == userId).ToListAsync(ct);
        if (bindings.Count == 0) return Array.Empty<string>();

        var roleIds = bindings.Select(x => x.RoleId).Distinct().ToArray();
        var rolePerms = await Db.Select<RolePermission>().Where(a => roleIds.Contains(a.RoleId)).ToListAsync(ct);
        if (rolePerms.Count == 0) return Array.Empty<string>();

        var permIds = rolePerms.Select(x => x.PermissionId).Distinct().ToArray();
        var perms = await Db.Select<Permission>().Where(a => permIds.Contains(a.Id)).ToListAsync(ct);

        return perms.Where(a => a.Status == 1 && !string.IsNullOrEmpty(a.Code))
            .Select(a => a.Code).Distinct().ToList();
    }

    /// <inheritdoc />
    public async Task<List<Role>> GetUserRolesAsync(long userId, CancellationToken ct = default)
    {
        var roleIds = await Db.Select<UserRole>().Where(a => a.UserId == userId).ToListAsync(ct);
        if (roleIds.Count == 0) return new List<Role>();

        var ids = roleIds.Select(x => x.RoleId).ToArray();
        return await Db.Select<Role>().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
    }

    /// <inheritdoc />
    public Task<int> ReplaceUserRolesAsync(long userId, IReadOnlyCollection<long> roleIds, long platformId, CancellationToken ct = default)
    {
        var ids = roleIds.Distinct().ToArray();
        var affected = 0;

        Db.Transaction(() =>
        {
            affected += Db.Delete<UserRole>().Where(a => a.UserId == userId).ExecuteAffrows();
            if (ids.Length == 0) return;

            var now = DateTime.UtcNow;
            affected += Db.Insert(new List<UserRole>(ids.Select(rid => new UserRole
            {
                UserId = userId,
                RoleId = rid,
                PlatformId = platformId,
                CreatedAt = now
            }))).ExecuteAffrows();
        });

        return Task.FromResult(affected);
    }

    /// <inheritdoc />
    public async Task<int> UnbindRoleAsync(long roleId, CancellationToken ct = default)
        => await Db.Delete<UserRole>().Where(a => a.RoleId == roleId).ExecuteAffrowsAsync(ct);
}
