using System;
using System.Threading.Tasks;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class ProductPriceChangedIntegrationEventHandler : IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>
    {
        private readonly IProductRepository _productRepository;
        private readonly ILogger<ProductPriceChangedIntegrationEventHandler> _logger;

        public ProductPriceChangedIntegrationEventHandler(
            IProductRepository productRepository,
            ILogger<ProductPriceChangedIntegrationEventHandler> logger)
        {
            _productRepository = productRepository;
            _logger = logger;
        }

        public async Task Handle(ProductPriceChangedIntegrationEvent @event)
        {
            // ❌ 修改前：需要在每个处理器中重复记录事件基本信息
            _logger.LogInformation("开始处理产品价格变更事件 - 产品ID: {ProductId}, 旧价格: {OldPrice}, 新价格: {NewPrice}",
                @event.ProductId, @event.OldPrice, @event.NewPrice);

            try
            {
                // 业务逻辑：更新产品价格
                var product = await _productRepository.GetByIdAsync(@event.ProductId);
                if (product == null)
                {
                    _logger.LogWarning("产品不存在 - 产品ID: {ProductId}", @event.ProductId);
                    return;
                }

                // 记录价格变更详情
                _logger.LogInformation("产品价格变更详情 - 产品: {ProductName}, 旧价格: {OldPrice}, 新价格: {NewPrice}",
                    product.Name, @event.OldPrice, @event.NewPrice);

                product.Price = @event.NewPrice;
                await _productRepository.UpdateAsync(product);

                // 记录成功日志
                _logger.LogInformation("产品价格更新成功 - 产品ID: {ProductId}, 新价格: {NewPrice}",
                    @event.ProductId, @event.NewPrice);

                // 可能还需要记录到审计日志
                await LogPriceChangeToAudit(@event, product.Name);

            }
            catch (Exception ex)
            {
                // 记录错误日志
                _logger.LogError(ex, "处理产品价格变更事件失败 - 产品ID: {ProductId}", @event.ProductId);
                throw;
            }
        }

        private async Task LogPriceChangeToAudit(ProductPriceChangedIntegrationEvent @event, string productName)
        {
            // 记录到审计系统的逻辑
            _logger.LogInformation("记录价格变更审计信息 - 产品: {ProductName}, 价格从 {OldPrice} 变更为 {NewPrice}",
                productName, @event.OldPrice, @event.NewPrice);
            // 实际的审计记录逻辑...
        }
    }
}