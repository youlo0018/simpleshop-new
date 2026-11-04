using Collaboration.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProductService.Application.Features.Search;

namespace ProductService.Api.Controllers;

/// <summary>商品搜索同义词配置。</summary>
[ApiController]
[Route("products/SearchIndex/Synonyms")]
public sealed class SearchSynonymController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public SearchSynonymController(IMediator mediator) => _mediator = mediator;

    /// <summary>读取当前同义词。</summary>
    [HttpGet("Get")]
    public Task<ApiResponse<SearchSynonymsDto>> Get(CancellationToken ct)
        => _mediator.Send(new QuerySearchSynonymsQuery(), ct);

    /// <summary>兼容旧版配置页：整批替换全局等价词。</summary>
    [HttpPost("Save")]
    public Task<ApiResponse<SearchSynonymsDto>> Save(
        [FromBody] SaveSearchSynonymsCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>查询结构化同义词规则。</summary>
    [HttpPost("Rules/List")]
    public Task<ApiResponse<List<SearchSynonymRuleDto>>> ListRules(
        [FromBody] QuerySearchSynonymRulesCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>新增或编辑同义词规则。</summary>
    [HttpPost("Rules/Save")]
    public Task<ApiResponse<SearchSynonymRuleDto>> SaveRule(
        [FromBody] SaveSearchSynonymRuleCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>删除同义词规则。</summary>
    [HttpPost("Rules/Delete")]
    public Task<ApiResponse> DeleteRule(
        [FromBody] DeleteSearchSynonymRuleCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>启停同义词规则。</summary>
    [HttpPost("Rules/SetStatus")]
    public Task<ApiResponse> SetRuleStatus(
        [FromBody] SetSearchSynonymRuleStatusCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>预览关键词的同义词扩展结果。</summary>
    [HttpPost("Preview")]
    public Task<ApiResponse<List<SearchSynonymPreviewDto>>> Preview(
        [FromBody] PreviewSearchSynonymCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
