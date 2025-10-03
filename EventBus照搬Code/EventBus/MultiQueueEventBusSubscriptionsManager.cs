using System;
using System.Collections.Generic;
using System.Linq;

namespace EventBus照搬Code.EventBus
{
    public class MultiQueueEventBusSubscriptionsManager : IEventBusSubscriptionsManager
    {
        // 新的数据结构：队列名称 → 事件名称 → 订阅列表
        private readonly Dictionary<string, Dictionary<string, List<SubscriptionInfo>>> _queueHandlers;
        private readonly List<Type> _eventTypes;

        public event EventHandler<string> OnEventRemoved;

        public MultiQueueEventBusSubscriptionsManager()
        {
            _queueHandlers = new Dictionary<string, Dictionary<string, List<SubscriptionInfo>>>();
            _eventTypes = new List<Type>();
        }

        public bool IsEmpty => !_queueHandlers.Any(q => q.Value.Any());

        public void Clear()
        {
            _queueHandlers.Clear();
            _eventTypes.Clear();
        }

        public void AddDynamicSubscription<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        // 新的多队列动态订阅方法
        public void AddDynamicSubscription<TH>(string queueName, string eventName) where TH : IDynamicIntegrationEventHandler
        {
            DoAddSubscription(typeof(TH), queueName, eventName, isDynamic: true);
        }

        public void AddSubscription<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        // 新的多队列订阅方法
        public void AddSubscription<T, TH>(string queueName)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = GetEventKey<T>();
            DoAddSubscription(typeof(TH), queueName, eventName, isDynamic: false);

            if (!_eventTypes.Contains(typeof(T)))
            {
                _eventTypes.Add(typeof(T));
            }
        }

        private void DoAddSubscription(Type handlerType, string queueName, string eventName, bool isDynamic)
        {
            // 确保队列字典存在
            if (!_queueHandlers.ContainsKey(queueName))
            {
                _queueHandlers[queueName] = new Dictionary<string, List<SubscriptionInfo>>();
            }

            // 确保事件字典存在
            if (!_queueHandlers[queueName].ContainsKey(eventName))
            {
                _queueHandlers[queueName][eventName] = new List<SubscriptionInfo>();
            }

            // 检查是否已注册
            if (_queueHandlers[queueName][eventName].Any(s => s.HandlerType == handlerType))
            {
                throw new ArgumentException($"处理器类型 {handlerType.Name} 已经在队列 '{queueName}' 的事件 '{eventName}' 中注册", nameof(handlerType));
            }

            if (isDynamic)
            {
                _queueHandlers[queueName][eventName].Add(SubscriptionInfo.Dynamic(handlerType));
            }
            else
            {
                _queueHandlers[queueName][eventName].Add(SubscriptionInfo.Typed(handlerType));
            }
        }

        public void RemoveDynamicSubscription<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        // 新的多队列动态取消订阅方法
        public void RemoveDynamicSubscription<TH>(string queueName, string eventName) where TH : IDynamicIntegrationEventHandler
        {
            var handlerToRemove = FindDynamicSubscriptionToRemove<TH>(queueName, eventName);
            DoRemoveHandler(queueName, eventName, handlerToRemove);
        }

        public void RemoveSubscription<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        // 新的多队列取消订阅方法
        public void RemoveSubscription<T, TH>(string queueName)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var handlerToRemove = FindSubscriptionToRemove<T, TH>(queueName);
            var eventName = GetEventKey<T>();
            DoRemoveHandler(queueName, eventName, handlerToRemove);
        }

        private void DoRemoveHandler(string queueName, string eventName, SubscriptionInfo subsToRemove)
        {
            if (subsToRemove != null && _queueHandlers.ContainsKey(queueName) && _queueHandlers[queueName].ContainsKey(eventName))
            {
                _queueHandlers[queueName][eventName].Remove(subsToRemove);

                if (!_queueHandlers[queueName][eventName].Any())
                {
                    _queueHandlers[queueName].Remove(eventName);

                    if (!_queueHandlers[queueName].Any())
                    {
                        _queueHandlers.Remove(queueName);
                    }

                    var eventType = _eventTypes.SingleOrDefault(e => e.Name == eventName);
                    if (eventType != null)
                    {
                        _eventTypes.Remove(eventType);
                    }

                    RaiseOnEventRemoved(eventName);
                }
            }
        }

        // 新的多队列获取处理器方法
        public IEnumerable<SubscriptionInfo> GetHandlersForEvent(string queueName, string eventName)
        {
            if (_queueHandlers.ContainsKey(queueName) && _queueHandlers[queueName].ContainsKey(eventName))
            {
                return _queueHandlers[queueName][eventName];
            }
            return Enumerable.Empty<SubscriptionInfo>();
        }

        // 保持向后兼容的方法
        public IEnumerable<SubscriptionInfo> GetHandlersForEvent<T>() where T : IntegrationEvent
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        public IEnumerable<SubscriptionInfo> GetHandlersForEvent(string eventName)
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        private void RaiseOnEventRemoved(string eventName)
        {
            var handler = OnEventRemoved;
            handler?.Invoke(this, eventName);
        }

        private SubscriptionInfo FindDynamicSubscriptionToRemove<TH>(string queueName, string eventName) where TH : IDynamicIntegrationEventHandler
        {
            return DoFindSubscriptionToRemove(queueName, eventName, typeof(TH));
        }

        private SubscriptionInfo FindSubscriptionToRemove<T, TH>(string queueName)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = GetEventKey<T>();
            return DoFindSubscriptionToRemove(queueName, eventName, typeof(TH));
        }

        private SubscriptionInfo DoFindSubscriptionToRemove(string queueName, string eventName, Type handlerType)
        {
            if (!HasSubscriptionsForEvent(queueName, eventName))
            {
                return null;
            }

            return _queueHandlers[queueName][eventName].SingleOrDefault(s => s.HandlerType == handlerType);
        }

        public bool HasSubscriptionsForEvent<T>() where T : IntegrationEvent
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        public bool HasSubscriptionsForEvent(string eventName)
        {
            throw new InvalidOperationException("请使用带队列名称的重载方法");
        }

        // 新的多队列检查订阅方法
        public bool HasSubscriptionsForEvent(string queueName, string eventName)
        {
            return _queueHandlers.ContainsKey(queueName) && _queueHandlers[queueName].ContainsKey(eventName);
        }

        public Type GetEventTypeByName(string eventName) => _eventTypes.SingleOrDefault(t => t.Name == eventName);

        public string GetEventKey<T>() => typeof(T).Name;
    }
}