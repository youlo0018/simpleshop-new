using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Collaboration.Domain.MediatR;

namespace PermissionService.Application.Features.Permission.DeletePermission;

/// <summary>删除权限点。仅超级管理员可执行；内置权限点被拒绝。</summary>
public record DeletePermissionCommand(long PermissionId) : IRequest<ApiResponse>, ISuperAdminOnly
{
    /// <summary>审计说明，写入越权日志。</summary>
    public string AuditNote => "删除权限点 " + PermissionId;
}

/// <summary>启用 / 停用权限点。仅超级管理员可执行。</summary>
/// <remarks>停用后网关不再校验该权限点绑定的路径（BUSINESS 5.4）。</remarks>
public record ChangePermissionStatusCommand(long PermissionId, int Status) : IRequest<ApiResponse>, ISuperAdminOnly
{
    /// <summary>审计说明，写入越权日志。</summary>
    public string AuditNote => "变更权限点状态 " + PermissionId + " -> " + Status;
}

/// <summary>删除权限点校验。</summary>
public sealed class DeletePermissionValidator : AbstractValidator<DeletePermissionCommand>
{
    /// <summary>构造校验器。</summary>
    public DeletePermissionValidator()
        => RuleFor(x => x.PermissionId).GreaterThan(0).WithMessage("权限点 Id 必须为正数");
}

/// <summary>启用 / 停用权限点校验。</summary>
public sealed class ChangePermissionStatusValidator : AbstractValidator<ChangePermissionStatusCommand>
{
    /// <summary>构造校验器。</summary>
    public ChangePermissionStatusValidator()
    {
        RuleFor(x => x.PermissionId).GreaterThan(0).WithMessage("权限点 Id 必须为正数");
        RuleFor(x => x.Status).InclusiveBetween(1, 2).WithMessage("状态只能是 1 启用 / 2 停用");
    }
}

