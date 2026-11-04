using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using Xunit;

namespace Collaboration.Domain.Tests;

/// <summary>C 端客户范围解析的安全边界测试。</summary>
public sealed class CustomerScopeTests
{
    /// <summary>客户令牌存在时，只能操作令牌里的客户 Id。</summary>
    [Fact]
    public void Require_CustomerToken_ShouldUseTokenCustomerAndRejectOtherCustomer()
    {
        TenantContextHolder.Set(new TenantContext
        {
            Access = AccessContext.Customer,
            TenantType = 3,
            UserId = 1001
        });

        try
        {
            Assert.Equal(1001, CustomerScope.Require(0));
            Assert.Equal(1001, CustomerScope.Require(1001));

            var exception = Assert.Throws<BaseApiException>(() => CustomerScope.Require(2002));
            Assert.Equal((int)BaseApiResponseCode.Forbidden, (int)exception.Code);
        }
        finally
        {
            TenantContextHolder.Clear();
        }
    }

    /// <summary>没有客户上下文时保留显式 CustomerId，供内部任务和直连测试使用。</summary>
    [Fact]
    public void Require_NoCustomerContext_ShouldAllowExplicitCustomerOnly()
    {
        TenantContextHolder.Clear();

        Assert.Equal(3003, CustomerScope.Require(3003));
        var exception = Assert.Throws<BaseApiException>(() => CustomerScope.Require(0));
        Assert.Equal((int)BaseApiResponseCode.Unauthorized, (int)exception.Code);
    }

    /// <summary>游客可用的只读接口：没有客户上下文时 0 仍是 0，不弹「请先登录」。</summary>
    [Fact]
    public void Resolve_NoCustomerContext_ShouldKeepGuestZero()
    {
        TenantContextHolder.Clear();

        Assert.Equal(0, CustomerScope.Resolve(0));
        Assert.Equal(3003, CustomerScope.Resolve(3003));
    }

    /// <summary>客户令牌存在时，Resolve 同样只认令牌客户：防止读他人券包 / 券码。</summary>
    [Fact]
    public void Resolve_CustomerToken_ShouldIgnoreRequestCustomer()
    {
        TenantContextHolder.Set(new TenantContext
        {
            Access = AccessContext.Customer,
            TenantType = 3,
            UserId = 1001
        });

        try
        {
            Assert.Equal(1001, CustomerScope.Resolve(0));
            Assert.Equal(1001, CustomerScope.Resolve(1001));

            var exception = Assert.Throws<BaseApiException>(() => CustomerScope.Resolve(2002));
            Assert.Equal((int)BaseApiResponseCode.Forbidden, (int)exception.Code);
        }
        finally
        {
            TenantContextHolder.Clear();
        }
    }
}
