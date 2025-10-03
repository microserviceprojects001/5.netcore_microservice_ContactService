using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class UserCheckoutAcceptedIntegrationEvent : IntegrationEvent
    {
        public string UserId { get; set; }

        public int OrderNumber { get; set; }

        public string City { get; set; }

        public string Street { get; set; }

        public string State { get; set; }

        public string Country { get; set; }

        public string ZipCode { get; set; }

        public string CardNumber { get; set; }

        public string CardHolderName { get; set; }


        public string CardExpiration { get; set; }

        public string CardSecurityNumber { get; set; }

        public string CardTypeId { get; set; }

        public string Buyer { get; set; }


        public int RequestId { get; set; }

        public CustomerBasket Basket { get; set; }
        public UserCheckoutAcceptedIntegrationEvent(string userId, int orderNumber, string city, string street,
        string state, string country, string zipCode, string cardNumber,
         string cardHolderName, string cardExpiration, string cardSecurityNumber,
         string cardTypeId, string buyer, int requestId, CustomerBasket basket)
        {
            UserId = userId;
            OrderNumber = orderNumber;
            City = city;
            Street = street;
            State = state;
            Country = country;
            ZipCode = zipCode;
            CardNumber = cardNumber;
            CardHolderName = cardHolderName;
            CardExpiration = cardExpiration;
            CardSecurityNumber = cardSecurityNumber;
            CardTypeId = cardTypeId;
            Buyer = buyer;
            RequestId = requestId;
            Basket = basket;
        }
    }
}