using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventBus照搬Code.EventBusRabbitMQ
{
    public class MultiQueueEventBusRabbitMQ : IEventBus, IDisposable
    {
        const string BROKER_NAME = "event_bus";

        private readonly IRabbitMQPersistentConnection _persistentConnection;
        private readonly ILogger<MultiQueueEventBusRabbitMQ> _logger;
        private readonly IEventBusSubscriptionsManager _subsManager;
        private readonly ILifetimeScope _autofacScope;
        private readonly int _retryCount;

        // 支持多个消费通道，每个队列一个
        private readonly ConcurrentDictionary<string, IModel> _consumerChannels = new();
        private string _defaultQueueName;

        public MultiQueueEventBusRabbitMQ(
            IRabbitMQPersistentConnection persistentConnection,
            ILogger<MultiQueueEventBusRabbitMQ> logger,
            IEventBusSubscriptionsManager subsManager,
            string defaultQueueName = null,
            int retryCount = 5,
            ILifetimeScope autofacScope = null)
        {
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
            _defaultQueueName = defaultQueueName;
            _retryCount = retryCount;
            _autofacScope = autofacScope;
            _subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
        }

        private void SubsManager_OnEventRemoved(object sender, string eventName)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            // 从所有队列中取消绑定
            foreach (var queueChannel in _consumerChannels)
            {
                using (var channel = _persistentConnection.CreateModel())
                {
                    channel.QueueUnbind(queue: queueChannel.Key,
                        exchange: BROKER_NAME,
                        routingKey: eventName);
                }
            }
        }

        public void Publish(IntegrationEvent @event)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, time) =>
                    {
                        _logger.LogWarning(ex, "Could not publish event: {EventId} after {Timeout}s ({ExceptionMessage})",
                            @event.Id, $"{time.TotalSeconds:n1}", ex.Message);
                    });

            var eventName = @event.GetType().Name;

            _logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id, eventName);

            using (var channel = _persistentConnection.CreateModel())
            {
                _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

                channel.ExchangeDeclare(exchange: BROKER_NAME, type: "direct");

                var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                policy.Execute(() =>
                {
                    var properties = channel.CreateBasicProperties();
                    properties.DeliveryMode = 2; // persistent

                    _logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);

                    channel.BasicPublish(
                        exchange: BROKER_NAME,
                        routingKey: eventName,
                        mandatory: true,
                        basicProperties: properties,
                        body: body);
                });
            }
        }

        // 默认订阅方法（使用默认队列）
        public void Subscribe<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            if (string.IsNullOrEmpty(_defaultQueueName))
            {
                throw new InvalidOperationException("默认队列名称未设置，请使用 SubscribeToQueue 方法指定队列名称");
            }

            SubscribeToQueue<T, TH>(_defaultQueueName);
        }

        // 新的多队列订阅方法
        public void SubscribeToQueue<T, TH>(string queueName)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = _subsManager.GetEventKey<T>();

            // 确保队列的消费通道存在
            EnsureConsumerChannelForQueue(queueName);

            DoInternalSubscription(queueName, eventName);

            _logger.LogInformation("在队列 {QueueName} 上订阅事件 {EventName} 使用处理器 {EventHandler}",
                queueName, eventName, typeof(TH).GetGenericTypeName());

            _subsManager.AddSubscription<T, TH>(queueName);
            StartBasicConsume(queueName);
        }

        public void Unsubscribe<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            if (string.IsNullOrEmpty(_defaultQueueName))
            {
                throw new InvalidOperationException("默认队列名称未设置，请使用 UnsubscribeFromQueue 方法指定队列名称");
            }

            UnsubscribeFromQueue<T, TH>(_defaultQueueName);
        }

        // 新的多队列取消订阅方法
        public void UnsubscribeFromQueue<T, TH>(string queueName)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = _subsManager.GetEventKey<T>();

            _logger.LogInformation("从队列 {QueueName} 取消订阅事件 {EventName}", queueName, eventName);

            _subsManager.RemoveSubscription<T, TH>(queueName);
        }

        public void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            if (string.IsNullOrEmpty(_defaultQueueName))
            {
                throw new InvalidOperationException("默认队列名称未设置，请使用 SubscribeDynamicToQueue 方法指定队列名称");
            }

            SubscribeDynamicToQueue<TH>(_defaultQueueName, eventName);
        }

        // 新的多队列动态订阅方法
        public void SubscribeDynamicToQueue<TH>(string queueName, string eventName) where TH : IDynamicIntegrationEventHandler
        {
            EnsureConsumerChannelForQueue(queueName);

            _logger.LogInformation("在队列 {QueueName} 上订阅动态事件 {EventName} 使用处理器 {EventHandler}",
                queueName, eventName, typeof(TH).GetGenericTypeName());

            DoInternalSubscription(queueName, eventName);
            _subsManager.AddDynamicSubscription<TH>(queueName, eventName);
            StartBasicConsume(queueName);
        }

        public void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            if (string.IsNullOrEmpty(_defaultQueueName))
            {
                throw new InvalidOperationException("默认队列名称未设置，请使用 UnsubscribeDynamicFromQueue 方法指定队列名称");
            }

            UnsubscribeDynamicFromQueue<TH>(_defaultQueueName, eventName);
        }

        // 新的多队列动态取消订阅方法
        public void UnsubscribeDynamicFromQueue<TH>(string queueName, string eventName) where TH : IDynamicIntegrationEventHandler
        {
            _subsManager.RemoveDynamicSubscription<TH>(queueName, eventName);
        }

        public void Dispose()
        {
            foreach (var channel in _consumerChannels.Values)
            {
                channel?.Dispose();
            }
            _consumerChannels.Clear();
            _subsManager.Clear();
        }

        private void EnsureConsumerChannelForQueue(string queueName)
        {
            if (!_consumerChannels.ContainsKey(queueName))
            {
                var channel = CreateConsumerChannel(queueName);
                _consumerChannels[queueName] = channel;
            }
        }

        private void DoInternalSubscription(string queueName, string eventName)
        {
            var containsKey = _subsManager.HasSubscriptionsForEvent(queueName, eventName);
            if (!containsKey)
            {
                if (!_persistentConnection.IsConnected)
                {
                    _persistentConnection.TryConnect();
                }

                var channel = _consumerChannels[queueName];
                channel.QueueBind(queue: queueName,
                                  exchange: BROKER_NAME,
                                  routingKey: eventName);
            }
        }

        private IModel CreateConsumerChannel(string queueName)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            _logger.LogTrace("为队列 {QueueName} 创建 RabbitMQ 消费通道", queueName);

            var channel = _persistentConnection.CreateModel();

            channel.ExchangeDeclare(exchange: BROKER_NAME, type: "direct");

            if (!string.IsNullOrEmpty(queueName))
            {
                channel.QueueDeclare(queue: queueName,
                                    durable: true,
                                    exclusive: false,
                                    autoDelete: false,
                                    arguments: null);
            }

            channel.CallbackException += (sender, ea) =>
            {
                _logger.LogWarning(ea.Exception, "重新创建 RabbitMQ 消费通道 for queue {QueueName}", queueName);

                if (_consumerChannels.TryRemove(queueName, out var oldChannel))
                {
                    oldChannel.Dispose();
                }

                var newChannel = CreateConsumerChannel(queueName);
                _consumerChannels[queueName] = newChannel;
                StartBasicConsume(queueName);
            };

            return channel;
        }

        private void StartBasicConsume(string queueName)
        {
            _logger.LogTrace("启动队列 {QueueName} 的 RabbitMQ 基础消费", queueName);

            if (_consumerChannels.TryGetValue(queueName, out var channel) && channel != null)
            {
                var consumer = new AsyncEventingBasicConsumer(channel);

                consumer.Received += async (model, ea) =>
                {
                    var eventName = ea.RoutingKey;
                    var message = Encoding.UTF8.GetString(ea.Body.Span);

                    await ProcessEvent(queueName, eventName, message);

                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                };

                channel.BasicConsume(
                    queue: queueName,
                    autoAck: false,
                    consumer: consumer);
            }
            else
            {
                _logger.LogError("无法在队列 {QueueName} 上启动基础消费，消费通道为 null", queueName);
            }
        }

        private async Task ProcessEvent(string queueName, string eventName, string message)
        {
            _logger.LogTrace("处理队列 {QueueName} 的 RabbitMQ 事件: {EventName}", queueName, eventName);

            if (_subsManager.HasSubscriptionsForEvent(queueName, eventName))
            {
                using (var scope = _autofacScope?.BeginLifetimeScope() ?? CreateScope())
                {
                    var subscriptions = _subsManager.GetHandlersForEvent(queueName, eventName);
                    foreach (var subscription in subscriptions)
                    {
                        if (subscription.IsDynamic)
                        {
                            var handler = scope.ResolveOptional(subscription.HandlerType) as IDynamicIntegrationEventHandler;
                            if (handler == null) continue;
                            dynamic eventData = JsonSerializer.Deserialize<dynamic>(message);
                            await handler.Handle(eventData);
                        }
                        else
                        {
                            var handler = scope.ResolveOptional(subscription.HandlerType);
                            if (handler == null) continue;
                            var eventType = _subsManager.GetEventTypeByName(eventName);
                            var integrationEvent = JsonSerializer.Deserialize(message, eventType, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                            var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

                            await (Task)concreteType.GetMethod("Handle").Invoke(handler, new object[] { integrationEvent });
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("队列 {QueueName} 没有对 RabbitMQ 事件 {EventName} 的订阅", queueName, eventName);
            }
        }

        private ILifetimeScope CreateScope()
        {
            // 如果没有使用Autofac，可以返回一个简单的scope
            // 这里需要根据你的DI容器来调整
            return null;
        }
    }

    internal static class TypeExtensions
    {
        public static string GetGenericTypeName(this Type type)
        {
            if (type.IsGenericType)
            {
                var genericTypes = string.Join(",", type.GetGenericArguments().Select(t => t.Name).ToArray());
                return $"{type.Name.Remove(type.Name.IndexOf('`'))}<{genericTypes}>";
            }
            else
            {
                return type.Name;
            }
        }
    }
}