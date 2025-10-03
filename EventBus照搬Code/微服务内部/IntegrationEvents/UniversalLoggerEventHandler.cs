using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class UniversalLoggerEventHandler : IDynamicIntegrationEventHandler
    {
        private readonly ILogger<UniversalLoggerEventHandler> _logger;

        public UniversalLoggerEventHandler(ILogger<UniversalLoggerEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(dynamic eventData)
        {
            try
            {
                // 安全地获取事件类型名称
                string eventType = GetEventTypeSafe(eventData);

                // 记录通用事件信息
                _logger.LogInformation(
                    "📥 事件接收 - 类型: {EventType}, ID: {EventId}, 时间: {CreationDate}",
                    eventType,
                    GetPropertySafe(eventData, "Id") ?? "Unknown",
                    GetPropertySafe(eventData, "CreationDate") ?? DateTime.UtcNow.ToString()
                );

                // 记录事件完整数据（调试级别，避免生产环境过多日志）
                _logger.LogDebug("事件完整数据: {EventData}",
                    JsonSerializer.Serialize(eventData, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

                // 根据事件类型进行特定处理
                ProcessEventByType(eventData, eventType);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理动态事件时发生错误");
            }

            return Task.CompletedTask;
        }

        private string GetEventTypeSafe(dynamic eventData)
        {
            try
            {
                return eventData?.GetType()?.Name ?? "UnknownEvent";
            }
            catch
            {
                return "UnknownEvent";
            }
        }

        private object GetPropertySafe(dynamic eventData, string propertyName)
        {
            try
            {
                return eventData[propertyName] ?? eventData.GetType().GetProperty(propertyName)?.GetValue(eventData);
            }
            catch
            {
                return null;
            }
        }

        private void ProcessEventByType(dynamic eventData, string eventType)
        {
            switch (eventType)
            {
                case "ProductPriceChangedIntegrationEvent":
                    ProcessProductPriceChanged(eventData);
                    break;
                case "OrderStartedIntegrationEvent":
                    ProcessOrderStarted(eventData);
                    break;
                case "UserCheckoutAcceptedIntegrationEvent":
                    ProcessUserCheckoutAccepted(eventData);
                    break;
                default:
                    _logger.LogDebug("未知事件类型: {EventType}", eventType);
                    break;
            }
        }

        private void ProcessProductPriceChanged(dynamic eventData)
        {
            try
            {
                var productId = (int?)eventData.ProductId;
                var oldPrice = (decimal?)eventData.OldPrice;
                var newPrice = (decimal?)eventData.NewPrice;

                if (productId.HasValue)
                {
                    _logger.LogInformation("💰 产品价格变更 - 产品ID: {ProductId}, 旧价格: {OldPrice}, 新价格: {NewPrice}, 变化幅度: {ChangePercent}%",
                        productId, oldPrice, newPrice,
                        oldPrice.HasValue && newPrice.HasValue ?
                        Math.Round(((double)(newPrice.Value - oldPrice.Value) / (double)oldPrice.Value * 100), 2) : 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理产品价格变更事件时发生错误");
            }
        }

        private void ProcessOrderStarted(dynamic eventData)
        {
            try
            {
                var orderId = (int?)eventData.OrderId;
                var orderDate = (DateTime?)eventData.OrderDate;

                if (orderId.HasValue)
                {
                    _logger.LogInformation("🛒 订单开始 - 订单ID: {OrderId}, 订单时间: {OrderDate}",
                        orderId, orderDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理订单开始事件时发生错误");
            }
        }

        private void ProcessUserCheckoutAccepted(dynamic eventData)
        {
            try
            {
                var userId = (string)eventData.UserId;
                var orderNumber = (int?)eventData.OrderNumber;

                _logger.LogInformation("✅ 用户结账接受 - 用户ID: {UserId}, 订单号: {OrderNumber}",
                    userId, orderNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理用户结账事件时发生错误");
            }
        }
    }
}