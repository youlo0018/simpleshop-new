using Collaboration.Domain.Common;

namespace Collaboration.Domain.Context;

/// <summary>
/// C 端客户范围解析。网关客户令牌存在时，以令牌里的客户 Id 为准。
/// </summary>
/// <remarks>
/// 这些接口的参数模型为了兼容服务直连与内部调用保留了 CustomerId，
/// 但经网关进入时必须防 IDOR：客户 A 改请求体里的 CustomerId 不能操作客户 B。
/// 没有客户上下文时仍允许显式传值，供内部任务与端到端测试直连使用。
/// </remarks>
public static class CustomerScope
{
    /// <summary>
    /// 解析当前请求实际可操作的客户 Id。
    /// </summary>
    /// <param name="requestedCustomerId">请求参数中的客户 Id；0 表示未显式指定。</param>
    /// <returns>最终使用的客户 Id。</returns>
    /// <exception cref="BaseApiException">请求客户与令牌客户不一致，或未登录且没有可用的显式客户。</exception>
    public static long Require(long requestedCustomerId)
    {
        var customerId = Resolve(requestedCustomerId);
        if (customerId <= 0)
        {
            throw new BaseApiException(BaseApiResponseCode.Unauthorized, "请先登录");
        }

        return customerId;
    }

    /// <summary>
    /// 解析当前请求实际可操作的客户 Id；<b>允许游客</b>（没有客户上下文时保留显式传值，0 仍是 0）。
    /// </summary>
    /// <param name="requestedCustomerId">请求参数中的客户 Id；0 表示游客 / 未显式指定。</param>
    /// <returns>最终使用的客户 Id；游客返回 0。</returns>
    /// <exception cref="BaseApiException">请求客户与令牌客户不一致。</exception>
    /// <remarks>
    /// 给「游客可用、登录后按令牌取客户」的只读接口用（到手价试算 / 结算试算）。
    /// 这些接口必须走这里而不是直接读请求体：不校验的话，客户 A 传客户 B 的 Id
    /// 就能读到 B 的券包与券码。与 <see cref="Require"/> 的差别只有一个：
    /// 没有客户上下文且请求 0（游客）时不抛「请先登录」。
    /// </remarks>
    public static long Resolve(long requestedCustomerId)
    {
        var context = TenantContextHolder.Current;
        if (context.IsCustomer)
        {
            if (requestedCustomerId > 0 && requestedCustomerId != context.UserId)
            {
                throw new BaseApiException(BaseApiResponseCode.Forbidden, "不能操作其他客户的数据");
            }

            return context.UserId;
        }

        return requestedCustomerId;
    }

    /// <summary>
    /// 解析<b>公开只读接口</b>（匿名可达）实际可用的客户 Id：有客户上下文用令牌客户，
    /// 没有就一律按游客（0）算。
    /// </summary>
    /// <param name="requestedCustomerId">请求参数里的客户 Id。</param>
    /// <returns>令牌里的客户 Id；没有客户上下文时返回 0。</returns>
    /// <exception cref="BaseApiException">请求客户与令牌客户不一致。</exception>
    /// <remarks>
    /// 商品列表 / 详情这类接口在网关是**匿名可达**的，没有客户令牌时下游拿不到任何客户上下文。
    /// 这时绝不能采信请求体里的 customerId：任何人填一个别人的 Id 就能读到对方的券包内容
    /// （实测商品卡会带出对方的券码）。所以「没有上下文 = 游客」，
    /// 登录客户要走这里就必须由网关把令牌验完并把客户 Id 注进来。
    /// </remarks>
    public static long ResolvePublic(long requestedCustomerId)
    {
        var context = TenantContextHolder.Current;
        if (context.IsCustomer)
        {
            if (requestedCustomerId > 0 && requestedCustomerId != context.UserId)
            {
                throw new BaseApiException(BaseApiResponseCode.Forbidden, "不能操作其他客户的数据");
            }

            return context.UserId;
        }

        return 0;
    }
}
