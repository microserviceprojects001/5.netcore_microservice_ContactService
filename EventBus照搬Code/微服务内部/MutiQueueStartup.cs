using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace EventBus照搬Code.微服务内部
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // 其他服务注册...

            // RabbitMQ 配置
            services.AddSingleton<IRabbitMQPersistentConnection>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<DefaultRabbitMQPersistentConnection>>();

                var factory = new ConnectionFactory()
                {
                    HostName = Configuration["EventBusConnection"] ?? "localhost",
                    DispatchConsumersAsync = true
                };

                if (!string.IsNullOrEmpty(Configuration["EventBusUserName"]))
                {
                    factory.UserName = Configuration["EventBusUserName"];
                }

                if (!string.IsNullOrEmpty(Configuration["EventBusPassword"]))
                {
                    factory.Password = Configuration["EventBusPassword"];
                }

                var retryCount = 5;
                if (!string.IsNullOrEmpty(Configuration["EventBusRetryCount"]))
                {
                    retryCount = int.Parse(Configuration["EventBusRetryCount"]);
                }

                return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
            });

            RegisterEventBus(services);
        }

        public void RegisterEventBus(IServiceCollection services)
        {
            var defaultQueueName = Configuration["SubscriptionClientName"] ?? "default_subscription";

            // 注册多队列 EventBus
            services.AddSingleton<IEventBus, MultiQueueEventBusRabbitMQ>(sp =>
            {
                var rabbitMQPersistentConnection = sp.GetRequiredService<IRabbitMQPersistentConnection>();
                var iLifetimeScope = sp.GetRequiredService<ILifetimeScope>();
                var logger = sp.GetRequiredService<ILogger<MultiQueueEventBusRabbitMQ>>();
                var eventBusSubcriptionsManager = sp.GetRequiredService<IEventBusSubscriptionsManager>();

                var retryCount = 5;
                if (!string.IsNullOrEmpty(Configuration["EventBusRetryCount"]))
                {
                    retryCount = int.Parse(Configuration["EventBusRetryCount"]);
                }

                return new MultiQueueEventBusRabbitMQ(
                    rabbitMQPersistentConnection,
                    logger,
                    eventBusSubcriptionsManager,
                    defaultQueueName,
                    retryCount,
                    iLifetimeScope);
            });

            // 注册多队列订阅管理器
            services.AddSingleton<IEventBusSubscriptionsManager, MultiQueueEventBusSubscriptionsManager>();

            // 注册事件处理器
            services.AddTransient<ProductPriceChangedIntegrationEventHandler>();
            services.AddTransient<OrderStartedIntegrationEventHandler>();
            services.AddTransient<UserCheckoutAcceptedIntegrationEventHandler>();

            // 注册新的处理器（用于不同队列）
            services.AddTransient<HighPriorityOrderHandler>();
            services.AddTransient<LowPriorityOrderHandler>();
            services.AddTransient<AnalyticsEventHandler>();

            // 注册动态事件处理器（通用功能）
            services.AddTransient<UniversalLoggerEventHandler>();
        }

        public void Configure(IApplicationBuilder app)
        {
            // 其他配置...

            ConfigureEventBus(app);
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>() as MultiQueueEventBusRabbitMQ;
            var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();

            if (eventBus == null)
            {
                logger.LogError("EventBus 不是 MultiQueueEventBusRabbitMQ 类型，无法配置多队列订阅");
                return;
            }

            // 🎯 方案1：使用专用处理器（推荐）
            // 高优先级队列订阅（实时处理）
            eventBus.SubscribeToQueue<OrderStartedIntegrationEvent, HighPriorityOrderStartedHandler>("high_priority_queue");
            eventBus.SubscribeToQueue<UserCheckoutAcceptedIntegrationEvent, HighPriorityUserCheckoutHandler>("high_priority_queue");

            // 🎯 低优先级队列订阅（批量处理）
            eventBus.SubscribeToQueue<ProductPriceChangedIntegrationEvent, LowPriorityOrderHandler>("low_priority_queue");
            eventBus.SubscribeToQueue<OrderStartedIntegrationEvent, AnalyticsEventHandler>("low_priority_queue");

            // 🎯 通用日志记录（所有队列都订阅）
            eventBus.SubscribeDynamicToQueue<UniversalLoggerEventHandler>("high_priority_queue", "OrderStartedIntegrationEvent");
            eventBus.SubscribeDynamicToQueue<UniversalLoggerEventHandler>("high_priority_queue", "UserCheckoutAcceptedIntegrationEvent");

            eventBus.SubscribeDynamicToQueue<UniversalLoggerEventHandler>("low_priority_queue", "ProductPriceChangedIntegrationEvent");
            eventBus.SubscribeDynamicToQueue<UniversalLoggerEventHandler>("low_priority_queue", "OrderStartedIntegrationEvent");

            logger.LogInformation("✅ 多队列事件总线订阅完成");
            logger.LogInformation("   - 高优先级队列: OrderStarted, UserCheckoutAccepted");
            logger.LogInformation("   - 低优先级队列: ProductPriceChanged, OrderStarted(分析)");
            logger.LogInformation("   - 通用日志记录: 所有事件类型");
        }
    }
}

// 高优先级订单开始处理器
public class HighPriorityOrderStartedHandler : IIntegrationEventHandler<OrderStartedIntegrationEvent>
{
    private readonly ILogger<HighPriorityOrderStartedHandler> _logger;

    public HighPriorityOrderStartedHandler(ILogger<HighPriorityOrderStartedHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(OrderStartedIntegrationEvent @event)
    {
        _logger.LogInformation("🔴 高优先级处理 - 订单开始: {OrderId}, 时间: {OrderDate}",
            @event.OrderId, @event.OrderDate);

        // 实时处理逻辑：库存检查、支付验证等
        await Task.Delay(100);
    }
}

// 高优先级用户结账处理器
public class HighPriorityUserCheckoutHandler : IIntegrationEventHandler<UserCheckoutAcceptedIntegrationEvent>
{
    private readonly ILogger<HighPriorityUserCheckoutHandler> _logger;

    public HighPriorityUserCheckoutHandler(ILogger<HighPriorityUserCheckoutHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(UserCheckoutAcceptedIntegrationEvent @event)
    {
        _logger.LogInformation("🔴 高优先级处理 - 用户结账: {UserId}, 订单号: {OrderNumber}",
            @event.UserId, @event.OrderNumber);

        // 实时处理逻辑：订单确认、库存预留等
        await Task.Delay(100);
    }
}

// 低优先级订单处理器  
public class LowPriorityOrderHandler : IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>
{
    private readonly ILogger<LowPriorityOrderHandler> _logger;

    public LowPriorityOrderHandler(ILogger<LowPriorityOrderHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(ProductPriceChangedIntegrationEvent @event)
    {
        _logger.LogInformation("🟢 低优先级处理 - 产品价格变更: {ProductId}, 新价格: {NewPrice}",
            @event.ProductId, @event.NewPrice);

        // 批量处理逻辑：数据分析、报表生成等
        await Task.Delay(500); // 模拟较长的处理时间
    }
}

// 分析事件处理器
public class AnalyticsEventHandler : IIntegrationEventHandler<OrderStartedIntegrationEvent>
{
    private readonly ILogger<AnalyticsEventHandler> _logger;

    public AnalyticsEventHandler(ILogger<AnalyticsEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(OrderStartedIntegrationEvent @event)
    {
        _logger.LogInformation("📊 分析处理 - 订单分析: {OrderId}", @event.OrderId);

        // 分析逻辑：用户行为分析、业务指标计算等
        await Task.Delay(300); // 模拟分析处理时间
    }
}