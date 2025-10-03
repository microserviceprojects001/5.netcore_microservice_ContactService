using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Autofac;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventBus照搬Code.EventBusServiceBus
{
    public class EventBusServiceBus : IEventBus, IAsyncDisposable
    {
        private readonly IServiceBusPersisterConnection _serviceBusPersisterConnection;
        private readonly ILogger<EventBusServiceBus> _logger;
        private readonly IEventBusSubscriptionsManager _subsManager;
        private readonly ILifetimeScope _autofacScope;
        private readonly string _subscriptionName;
        private readonly string _topicName = "event_bus";
        private readonly EventBusServiceBusConfig _config;

        private SubscriptionClient _subscriptionClient;
        private TopicClient _topicClient;

        public EventBusServiceBus(
            IServiceBusPersisterConnection serviceBusPersisterConnection,
            ILogger<EventBusServiceBus> logger,
            IEventBusSubscriptionsManager subsManager,
            string subscriptionName,
            ILifetimeScope autofacScope,
            IOptions<EventBusServiceBusConfig> config = null)
        {
            _serviceBusPersisterConnection = serviceBusPersisterConnection ?? throw new ArgumentNullException(nameof(serviceBusPersisterConnection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
            _subscriptionName = subscriptionName;
            _autofacScope = autofacScope;
            _config = config?.Value ?? new EventBusServiceBusConfig();

            _topicClient = _serviceBusPersisterConnection.CreateModel();
            _subscriptionClient = new SubscriptionClient(
                _serviceBusPersisterConnection.ServiceBusConnectionString,
                _topicName,
                _subscriptionName);

            RemoveDefaultRuleAsync().GetAwaiter().GetResult();
            RegisterSubscriptionClientMessageHandler();
        }

        public async Task PublishAsync(IntegrationEvent @event)
        {
            var eventName = @event.GetType().Name;
            var jsonMessage = JsonConvert.SerializeObject(@event);
            var body = Encoding.UTF8.GetBytes(jsonMessage);

            var message = new Message
            {
                MessageId = Guid.NewGuid().ToString(),
                Body = body,
                Label = eventName,
                ContentType = "application/json"
            };

            try
            {
                await _topicClient.SendAsync(message);
                _logger.LogDebug("Published event: {EventName} with ID: {MessageId}", eventName, message.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish event: {EventName}", eventName);
                throw;
            }
        }

        public async Task SubscribeAsync<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = typeof(T).Name;

            var containsKey = _subsManager.HasSubscriptionsForEvent<T>();
            if (!containsKey)
            {
                try
                {
                    await _subscriptionClient.AddRuleAsync(new RuleDescription
                    {
                        Filter = new CorrelationFilter { Label = eventName },
                        Name = eventName
                    });
                }
                catch (ServiceBusException)
                {
                    _logger.LogWarning("The messaging entity {eventName} already exists.", eventName);
                }
            }

            _logger.LogInformation("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).Name);
            _subsManager.AddSubscription<T, TH>();
        }

        public async Task UnsubscribeAsync<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = typeof(T).Name;

            try
            {
                await _subscriptionClient.RemoveRuleAsync(eventName);
            }
            catch (MessagingEntityNotFoundException)
            {
                _logger.LogWarning("The messaging entity {eventName} Could not be found.", eventName);
            }

            _logger.LogInformation("Unsubscribing from event {EventName}", eventName);
            _subsManager.RemoveSubscription<T, TH>();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _subsManager.Clear();

                if (_subscriptionClient != null && !_subscriptionClient.IsClosedOrClosing)
                {
                    await _subscriptionClient.CloseAsync();
                }

                if (_topicClient != null && !_topicClient.IsClosedOrClosing)
                {
                    await _topicClient.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing Service Bus clients");
            }
        }

        private void RegisterSubscriptionClientMessageHandler()
        {
            _subscriptionClient.RegisterMessageHandler(
                async (message, token) =>
                {
                    try
                    {
                        var eventName = message.Label;
                        var messageData = Encoding.UTF8.GetString(message.Body);

                        _logger.LogDebug("Processing ServiceBus event: {EventName}", eventName);

                        if (await ProcessEvent(eventName, messageData))
                        {
                            await _subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
                            _logger.LogDebug("Completed processing event: {EventName}", eventName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message");
                        // 考虑将消息移动到死信队列
                        await _subscriptionClient.DeadLetterAsync(
                            message.SystemProperties.LockToken,
                            "ProcessingFailed",
                            ex.Message);
                    }
                },
                new MessageHandlerOptions(ExceptionReceivedHandler)
                {
                    MaxConcurrentCalls = _config.MaxConcurrentCalls,
                    AutoComplete = false,
                    MaxAutoRenewDuration = TimeSpan.FromMinutes(10)
                });
        }

        private async Task ExceptionReceivedHandler(ExceptionReceivedEventArgs args)
        {
            var ex = args.Exception;
            var context = args.ExceptionReceivedContext;

            _logger.LogError(ex,
                "Message handling failed: {ExceptionMessage} | Endpoint: {Endpoint} | Entity: {Entity} | Action: {Action}",
                ex.Message, context.Endpoint, context.EntityPath, context.Action);

            await Task.CompletedTask;
        }

        private async Task<bool> ProcessEvent(string eventName, string message)
        {
            var processed = false;

            if (_subsManager.HasSubscriptionsForEvent(eventName))
            {
                using (var scope = _autofacScope?.BeginLifetimeScope() ?? CreateScope())
                {
                    var subscriptions = _subsManager.GetHandlersForEvent(eventName);

                    // 并行处理事件（如果支持）
                    var tasks = new List<Task>();

                    foreach (var subscription in subscriptions)
                    {
                        if (subscription.IsDynamic)
                        {
                            var handler = scope.ResolveOptional(subscription.HandlerType) as IDynamicIntegrationEventHandler;
                            if (handler != null)
                            {
                                dynamic eventData = JObject.Parse(message);
                                tasks.Add(handler.Handle(eventData));
                            }
                        }
                        else
                        {
                            var handler = scope.ResolveOptional(subscription.HandlerType);
                            if (handler != null)
                            {
                                var eventType = _subsManager.GetEventTypeByName(eventName);
                                var integrationEvent = JsonConvert.DeserializeObject(message, eventType);
                                var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

                                tasks.Add((Task)concreteType.GetMethod("Handle").Invoke(handler, new object[] { integrationEvent }));
                            }
                        }
                    }

                    await Task.WhenAll(tasks);
                    processed = true;
                }
            }
            else
            {
                _logger.LogWarning("No subscription for ServiceBus event: {EventName}", eventName);
            }

            return processed;
        }

        private async Task RemoveDefaultRuleAsync()
        {
            try
            {
                await _subscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName);
            }
            catch (MessagingEntityNotFoundException)
            {
                _logger.LogDebug("The default rule was already removed.");
            }
        }

        private ILifetimeScope CreateScope()
        {
            // 根据你的DI容器实现
            return null;
        }

        // 为了向后兼容，保留同步方法
        public void Publish(IntegrationEvent @event) => PublishAsync(@event).GetAwaiter().GetResult();
        public void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
            => SubscribeAsync<T, TH>().GetAwaiter().GetResult();
        public void Unsubscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
            => UnsubscribeAsync<T, TH>().GetAwaiter().GetResult();
        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();
    }

    public class EventBusServiceBusConfig
    {
        public int MaxConcurrentCalls { get; set; } = Environment.ProcessorCount;
        public int PrefetchCount { get; set; } = 0;
    }
}