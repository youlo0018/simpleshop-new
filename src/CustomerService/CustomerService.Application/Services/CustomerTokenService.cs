using System.Security.Claims;
using System.Text;
using Collaboration.Domain.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CustomerService.Application.Services;

/// <summary>签发结果：令牌本体 + 会话 Id + 过期时刻。</summary>
/// <param name="Token">JWT 字符串。</param>
/// <param name="SessionId">会话 Id（令牌里的 jti），服务端会话键与登出都靠它定位。</param>
/// <param name="ExpiresAt">过期时刻（UTC），调用方据此写会话 TTL。</param>
public sealed record IssuedCustomerToken(string Token, string SessionId, DateTime ExpiresAt);

/// <summary>客户令牌签发：HS256，声明含 tenant_type=customer（DATA_SPEC 4）。</summary>
/// <remarks>与后台令牌严格分离：后台走 AuthService 的 OpenIddict + RS256，不共用密钥与签发方。</remarks>
public sealed class CustomerTokenService
{
    /// <summary>租户类型声明值，标记这是客户令牌。</summary>
    public const string CustomerTenantType = "customer";

    private readonly JwtOptions _options;

    /// <summary>用配置构造。</summary>
    /// <param name="options">Jwt 配置，签发前由启动时序校验非空。</param>
    public CustomerTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    /// <summary>签发客户令牌。</summary>
    /// <param name="customerId">客户 Id，写入 sub 声明。</param>
    /// <param name="customerNo">客户唯一编码，写入 customer_no 声明。</param>
    /// <param name="platformId">所属平台 Id，写入 platform_id 声明（客户账号是平台内账号）。</param>
    /// <returns>令牌 + 会话 Id + 过期时刻。<b>无副作用</b>（写会话键由调用方负责）。</returns>
    /// <remarks>
    /// 每次签发都带一个新的 <c>jti</c>：同一客户多设备登录各占一把会话键，
    /// 登出 / 刷新只影响自己那一把，不会把其它设备一起踢下线。
    /// </remarks>
    public IssuedCustomerToken Issue(long customerId, string customerNo, long platformId)
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid().ToString("N");
        var expiresAt = now.AddHours(_options.ExpireHours);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = SigningKey(),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = customerId.ToString(),
                ["customer_no"] = customerNo,
                ["tenant_type"] = CustomerTenantType,
                // 平台内账号：网关会把 platform_id 注入下游，C 端查询据此确定平台维度
                ["platform_id"] = platformId.ToString(),
                // jti 就是服务端会话 Id：网关拿它拼会话键做校验与滑动续期
                ["jti"] = sessionId
            }
        };
        return new IssuedCustomerToken(
            new JsonWebTokenHandler().CreateToken(descriptor), sessionId, expiresAt);
    }

    /// <summary>构造客户身份，供网关校验令牌后使用。</summary>
    /// <param name="customerId">客户 Id。</param>
    /// <param name="platformId">所属平台 Id。</param>
    /// <returns>ClaimsPrincipal。</returns>
    public static ClaimsPrincipal BuildPrincipal(long customerId, long platformId = 0)
    {
        var claims = new[]
        {
            new Claim("sub", customerId.ToString()),
            new Claim("tenant_type", CustomerTenantType),
            new Claim("platform_id", platformId.ToString())
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "customer-jwt"));
    }

    /// <summary>构造 HS256 签名凭据（密钥来自 Jwt:Secret，网关用同一个值验签）。</summary>
    /// <returns>签名凭据。</returns>
    private SigningCredentials SigningKey()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }
}

