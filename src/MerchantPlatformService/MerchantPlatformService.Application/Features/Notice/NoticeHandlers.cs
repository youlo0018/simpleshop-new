using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using MediatR;
using MerchantPlatformService.Application.Features.Platform;
using MerchantPlatformService.Domain.Entities;
using MerchantPlatformService.Domain.IRepository;

namespace MerchantPlatformService.Application.Features.Notice;

/// <summary>新建公告处理器。</summary>
public sealed class CreateNoticeHandler
    : IRequestHandler<CreateNoticeCommand, ApiResponse<long>>
{
    private readonly IPlatformNoticeRepository _notices;
    private readonly IPlatformRepository _platforms;

    /// <summary>构造处理器。</summary>
    /// <param name="notices">公告仓储。</param>
    /// <param name="platforms">平台仓储。</param>
    public CreateNoticeHandler(
        IPlatformNoticeRepository notices, IPlatformRepository platforms)
    {
        _notices = notices;
        _platforms = platforms;
    }

    /// <summary>执行新建。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新公告 Id。</returns>
    public async Task<ApiResponse<long>> Handle(CreateNoticeCommand request, CancellationToken ct)
    {
        var ctx = TenantContextHolder.Current;

        // 平台公告是平台级内容，商户账号一律拒绝（即使权限点被误授予）
        if (ctx.IsMerchant)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.Forbidden, "商户账号不能维护平台公告");
        }

        // 平台账号强制本平台；超管必须显式指定平台
        var platformId = ctx.IsPlatform ? ctx.PlatformId : request.PlatformId;
        if (platformId <= 0)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.BadRequest, "请选择要维护公告的平台");
        }

        var platform = await _platforms.GetByIdAsync(platformId, ct).ConfigureAwait(false);
        if (platform is null)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.NotFound, "平台不存在");
        }

        if (platform.Status != PlatformStatuses.Enabled)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.BusinessError, "平台已停用，不能新增公告");
        }

        var id = await _notices.InsertAsync(new PlatformNotice
        {
            PlatformId = platformId,
            MerchantId = 0,
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            LinkType = request.LinkType,
            LinkParam = (request.LinkParam ?? string.Empty).Trim(),
            SortOrder = request.SortOrder,
            Status = request.Status
        }, ct).ConfigureAwait(false);

        return ApiResults.Ok(id, "公告已保存");
    }
}

/// <summary>编辑公告处理器。</summary>
public sealed class UpdateNoticeHandler : IRequestHandler<UpdateNoticeCommand, ApiResponse>
{
    private readonly IPlatformNoticeRepository _notices;

    /// <summary>构造处理器。</summary>
    /// <param name="notices">公告仓储。</param>
    public UpdateNoticeHandler(IPlatformNoticeRepository notices) => _notices = notices;

    /// <summary>执行编辑。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(UpdateNoticeCommand request, CancellationToken ct)
    {
        if (TenantContextHolder.Current.IsMerchant)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.Forbidden, "商户账号不能维护平台公告");
        }

        var notice = await _notices.GetByIdAsync(request.NoticeId, ct).ConfigureAwait(false);
        if (notice is null)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "公告不存在");
        }

        notice.Title = request.Title.Trim();
        notice.Content = request.Content.Trim();
        notice.LinkType = request.LinkType;
        notice.LinkParam = (request.LinkParam ?? string.Empty).Trim();
        notice.SortOrder = request.SortOrder;
        notice.Status = request.Status;

        var affected = await _notices.UpdateAsync(notice, ct).ConfigureAwait(false);
        return affected > 0
            ? ApiResponseFactory.Ok("保存成功")
            : ApiResponseFactory.Fail(BaseApiResponseCode.InternalError, "保存失败，请重试");
    }
}

/// <summary>删除公告处理器。</summary>
public sealed class DeleteNoticeHandler : IRequestHandler<DeleteNoticeCommand, ApiResponse>
{
    private readonly IPlatformNoticeRepository _notices;

    /// <summary>构造处理器。</summary>
    /// <param name="notices">公告仓储。</param>
    public DeleteNoticeHandler(IPlatformNoticeRepository notices) => _notices = notices;

    /// <summary>执行软删。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(DeleteNoticeCommand request, CancellationToken ct)
    {
        if (TenantContextHolder.Current.IsMerchant)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.Forbidden, "商户账号不能维护平台公告");
        }

        var ok = await _notices.SoftDeleteAsync(request.NoticeId, ct).ConfigureAwait(false);
        return ok
            ? ApiResponseFactory.Ok("删除成功")
            : ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "公告不存在");
    }
}

/// <summary>启停公告处理器。</summary>
public sealed class SetNoticeStatusHandler : IRequestHandler<SetNoticeStatusCommand, ApiResponse>
{
    private readonly IPlatformNoticeRepository _notices;

    /// <summary>构造处理器。</summary>
    /// <param name="notices">公告仓储。</param>
    public SetNoticeStatusHandler(IPlatformNoticeRepository notices) => _notices = notices;

    /// <summary>执行启停。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(SetNoticeStatusCommand request, CancellationToken ct)
    {
        if (TenantContextHolder.Current.IsMerchant)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.Forbidden, "商户账号不能维护平台公告");
        }

        var ok = await _notices.SetStatusAsync(request.NoticeId, request.Status, ct).ConfigureAwait(false);
        return ok
            ? ApiResponseFactory.Ok("保存成功")
            : ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "公告不存在");
    }
}

/// <summary>后台公告分页处理器。</summary>
public sealed class QueryNoticesHandler
    : IRequestHandler<QueryNoticesCommand, ApiResponse<PagedResult<NoticeListItemDto>>>
{
    private readonly IPlatformNoticeRepository _notices;

    /// <summary>构造处理器。</summary>
    /// <param name="notices">公告仓储。</param>
    public QueryNoticesHandler(IPlatformNoticeRepository notices) => _notices = notices;

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>公告分页。</returns>
    public async Task<ApiResponse<PagedResult<NoticeListItemDto>>> Handle(
        QueryNoticesCommand request, CancellationToken ct)
    {
        var (items, total) = await _notices
            .PageAsync(request.Status, request.Page, request.PageSize, ct)
            .ConfigureAwait(false);

        var dtos = items.Select(a => new NoticeListItemDto(
            a.Id.ToString(), a.Title, a.Content, a.LinkType, a.LinkParam,
            a.SortOrder, a.Status, PlatformNoticeStatuses.NameOf(a.Status),
            a.PlatformId, PlatformAssembler.FormatTime(a.CreatedAt))).ToList();

        return ApiResults.Ok(new PagedResult<NoticeListItemDto>(
            dtos, total, request.Page, request.PageSize));
    }
}

/// <summary>小程序公告处理器。</summary>
public sealed class QueryPublicNoticesHandler
    : IRequestHandler<QueryPublicNoticesCommand, ApiResponse<List<PublicNoticeDto>>>
{
    private readonly IPlatformNoticeRepository _notices;
    private readonly IPlatformRepository _platforms;

    /// <summary>构造处理器。</summary>
    /// <param name="notices">公告仓储。</param>
    /// <param name="platforms">平台仓储。</param>
    public QueryPublicNoticesHandler(
        IPlatformNoticeRepository notices, IPlatformRepository platforms)
    {
        _notices = notices;
        _platforms = platforms;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启用中的公告列表。</returns>
    public async Task<ApiResponse<List<PublicNoticeDto>>> Handle(
        QueryPublicNoticesCommand request, CancellationToken ct)
    {
        var platform = await _platforms
            .GetByCodeAsync(request.PlatformCode.Trim(), ct)
            .ConfigureAwait(false);

        if (platform is null || platform.Status != PlatformStatuses.Enabled)
        {
            return ApiResults.Fail<List<PublicNoticeDto>>(
                BaseApiResponseCode.NotFound, "平台不存在或已停用");
        }

        var items = await _notices
            .ListEnabledAsync(platform.Id, request.Limit, ct)
            .ConfigureAwait(false);

        return ApiResults.Ok(items.Select(a => new PublicNoticeDto(
            a.Id.ToString(), a.Title, a.Content, a.LinkType, a.LinkParam)).ToList());
    }
}
