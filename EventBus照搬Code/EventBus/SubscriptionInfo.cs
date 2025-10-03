using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus照搬Code
{
    public class SubscriptionInfo
    {
        public Type HandlerType { get; }
        public bool IsDynamic { get; }

        private SubscriptionInfo(Type handlerType, bool isDynamic)
        {
            HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
            IsDynamic = isDynamic;
        }

        public static SubscriptionInfo Dynamic(Type handlerType) => new SubscriptionInfo(handlerType, true);

        public static SubscriptionInfo Typed(Type handlerType) => new SubscriptionInfo(handlerType, false);
    }
}