using Collaboration.Domain.Common;
using MarketingService.Application.Features.Coupon;
using MarketingService.Domain.Entities;
using MarketingService.Domain.IRepository;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MarketingService.Api.Controllers;

/// <summary>券模板与券活动（后台）。</summary>
[ApiController]
[Route("marketing")]
public sealed class CouponAdminController : ControllerBase
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionRepository _promotions;
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="coupons">券仓储。</param>
    /// <param name="promotions">活动仓储，用于营销配置。</param>
    /// <param name="mediator">MediatR 入口。</param>
    public CouponAdminController(
        ICouponRepository coupons, IPromotionRepository promotions, IMediator mediator)
    {
        _coupons = coupons;
        _promotions = promotions;
        _mediator = mediator;
    }

    /// <summary>新建券模板。</summary>
    /// <param name="command">模板。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新模板 Id。</returns>
    /// <remarks>
    /// <para>改模板不影响已发出的券——已发的券按自己的快照算（DATA_SPEC 5.12），
    /// 所以这里不做任何「同步到已发券」的动作。</para>
    ///
    /// <para><b>走命令 + 校验器，不再直接收实体</b>：原来把 <c>CouponTemplate</c> 实体
    /// 绑请求体再 <c>Insert</c>，等于「创建」这条路绕过了全部校验（编辑那条路是有的）：
    /// 满赠券可以不选赠送模板、满减券可以不填优惠金额、<c>IssuedQuantity</c> 还能由
    /// 请求体直接写。同一个表单两条路径规则不一致，缺陷只会从松的那侧漏出来。</para>
    /// </remarks>
    [HttpPost("coupon-templates/Create")]
    public Task<ApiResponse<long>> CreateTemplate(
        [FromBody] CreateCouponTemplateCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>分页查询券模板。</summary>
    /// <param name="command">查询命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>券模板分页结果。</returns>
    /// <remarks>
    /// 列表里带出 <c>IssuedQuantity</c>（已发放数）与模板规则，
    /// 运营才能在列表上直接判断「这个券还剩多少、门槛是多少」，
    /// 不必逐个点进详情。
    /// </remarks>
    [HttpPost("coupon-templates/List")]
    public Task<ApiResponse<PagedResult<CouponTemplateItem>>> ListTemplates(
        [FromBody] QueryCouponTemplatesCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>券模板下拉（DATA_SPEC 4.2）：只返回启用模板。</summary>
    /// <param name="keyword">按模板名模糊搜索。</param>
    /// <param name="limit">最多返回多少条，1-200。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下拉项 <c>{ id, name, couponType, couponTypeName }</c>。</returns>
    /// <remarks>
    /// 券活动与满赠活动都要选模板，走列表接口再自己过滤的话，
    /// 下拉里会出现停用模板 —— 选中后用户点「领取」只会拿到「模板不存在或已停用」。
    /// </remarks>
    [HttpGet("coupon-templates/Options")]
    public Task<ApiResponse<List<CouponTemplateOption>>> TemplateOptions(
        [FromQuery] string keyword = "",
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
        => _mediator.Send(new QueryCouponTemplateOptionsCommand(keyword, limit), ct);

    /// <summary>编辑券模板。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    /// <remarks>
    /// <b>改模板不影响已发出的券</b>（DATA_SPEC 5.12 快照机制），
    /// 所以这里不做任何「同步到已发券」的动作，返回消息里也明确提示了这一点。
    /// </remarks>
    [HttpPost("coupon-templates/Update")]
    public Task<ApiResponse> UpdateTemplate(
        [FromBody] UpdateCouponTemplateCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>删除券模板（软删）。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    /// <remarks>已发出的券有快照，删模板不影响它们计算与展示。停发请改状态为「停用」。</remarks>
    [HttpPost("coupon-templates/Delete")]
    public Task<ApiResponse> DeleteTemplate(
        [FromBody] DeleteCouponTemplateCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>查询券模板。</summary>
    /// <param name="templateId">模板 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>模板。</returns>
    [HttpGet("coupon-templates/Get")]
    public async Task<ActionResult<ApiResponse<CouponTemplate>>> GetTemplate(
        [FromQuery] long templateId, CancellationToken ct)
    {
        var row = await _coupons.GetTemplateAsync(templateId, ct).ConfigureAwait(false);
        return row is null ? NotFound(Envelope("券模板不存在")) : Ok(ApiResults.Ok(row));
    }

    /// <summary>新建券活动。</summary>
    /// <param name="command">券活动。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新活动 Id。</returns>
    /// <remarks>
    /// <para>时间由处理器归一到 UTC 再落库（见 <c>CouponTimeNormalizer</c>）。</para>
    ///
    /// <para><b>走命令 + 校验器，不再直接收实体</b>：与券模板的创建同一处问题 ——
    /// 实体直接 <c>Insert</c> 绕过了全部校验，<c>ClaimedQuantity</c>（已领取数）
    /// 还能被请求体直接写。同一个表单两条路径规则不一致，缺陷只会从松的那侧漏出来。</para>
    /// </remarks>
    [HttpPost("coupon-activities/Create")]
    public Task<ApiResponse<long>> CreateActivity(
        [FromBody] CreateCouponActivityCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>分页查询券活动。</summary>
    /// <param name="command">查询命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>券活动分页结果，含关联模板名。</returns>
    /// <remarks>
    /// 列表冗余了 <c>TemplateName</c>（DATA_SPEC 4.1「下拉框显示 name，绝不显示 id」）：
    /// 券活动名字常年起得很随意，只显示模板 Id 的列表等于让人去猜。
    /// </remarks>
    [HttpPost("coupon-activities/List")]
    public Task<ApiResponse<PagedResult<CouponActivityItem>>> ListActivities(
        [FromBody] QueryCouponActivitiesCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>编辑券活动。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    /// <remarks>
    /// 🔴 <b>发放量不许改到小于已领取数</b>：活动池子是独立计数，
    /// 调到比已领取还小，报表上就会凭空多出一堆不存在的券。
    /// 这是唯一一条后台不能自由修改的字段。
    /// </remarks>
    [HttpPost("coupon-activities/Update")]
    public Task<ApiResponse> UpdateActivity(
        [FromBody] UpdateCouponActivityCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>启用 / 停用券活动。</summary>
    /// <param name="command">活动 Id 与目标状态。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    /// <remarks>
    /// <para>列表页的「启用 / 停用」按钮走这里，<b>不走 Update</b>：
    /// 编辑接口会重跑「发放量不得超过模板剩余可发量」，而活动创建时已经占过池子，
    /// 合法活动再点一次启用就会被自己挡成 400。</para>
    ///
    /// <para>停用永远放行；启用时只额外要求关联模板仍然存在。</para>
    /// </remarks>
    [HttpPost("coupon-activities/SetStatus")]
    public Task<ApiResponse> SetActivityStatus(
        [FromBody] SetCouponActivityStatusCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>分页查询券核销记录。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>用户券分页结果。</returns>
    /// <remarks>
    /// 数据源是 <c>user_coupon</c>（一行 = 一张已发出的券），
    /// 不另建记录表——两张表并存只会带来「两处数据对不上」的老问题。
    /// </remarks>
    [HttpPost("coupon-records/List")]
    public Task<ApiResponse<PagedResult<CouponRecordItem>>> ListCouponRecords(
        [FromBody] QueryCouponRecordsCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>查询券活动。</summary>
    /// <param name="activityId">活动 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>券活动。</returns>
    [HttpGet("coupon-activities/Get")]
    public async Task<ActionResult<ApiResponse<CouponActivity>>> GetActivity(
        [FromQuery] long activityId, CancellationToken ct)
    {
        var row = await _coupons.GetActivityAsync(activityId, ct).ConfigureAwait(false);
        return row is null ? NotFound(Envelope("券活动不存在")) : Ok(ApiResults.Ok(row));
    }

    /// <summary>读取平台优惠优先级。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>1 活动优先 / 2 券优先。</returns>
    [HttpGet("marketing-config/Get")]
    public async Task<ActionResult<ApiResponse<MarketingConfig>>> GetConfig(
        [FromQuery] long platformId, CancellationToken ct)
    {
        var row = await _promotions.GetConfigAsync(platformId, ct).ConfigureAwait(false);
        // 没配过就是默认「券优先」，返回默认行而不是 404
        return Ok(ApiResults.Ok(row ?? new MarketingConfig { PlatformId = platformId, Priority = MarketingPriorities.CouponFirst }));
    }

    /// <summary>保存平台优惠优先级。</summary>
    /// <param name="config">配置。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存结果。</returns>
    [HttpPost("marketing-config/Save")]
    public async Task<ActionResult<ApiResponse>> SaveConfig(
        [FromBody] MarketingConfig config, CancellationToken ct)
    {
        if (config.Priority is not (MarketingPriorities.ActivityFirst or MarketingPriorities.CouponFirst))
        {
            return BadRequest(Envelope("优先级只能是 1 活动优先 或 2 券优先"));
        }

        await _promotions.SavePriorityAsync(config.PlatformId, config.Priority, ct).ConfigureAwait(false);

        return Ok(ApiResults.Ok());
    }

    /// <summary>构造「参数不合法」的错误信封（与本项目统一响应体之外的少数控制器内校验共用）。</summary>
    /// <param name="message">面向用户的中文消息。</param>
    /// <returns>错误信封。</returns>
    private static Dictionary<string, object> Envelope(string message)
        => new() { ["error"] = "invalid_request", ["error_description"] = message };
}
