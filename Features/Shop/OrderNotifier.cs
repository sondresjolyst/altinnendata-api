using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Features.Shop
{
    public interface IOrderNotifier
    {
        Task OrderReservedAsync(int orderId, CancellationToken ct);
        Task OrderCancelledAsync(int orderId, CancellationToken ct);
        Task OrderRefundedAsync(int orderId, int amountInOre, CancellationToken ct);
        Task OrderShippedAsync(int orderId, CancellationToken ct);

        /// <summary>Vipps ended the reservation of a Reserved order on its own. <paramref name="vippsEvent"/> is the event or state that ended it.</summary>
        Task ReservationEndedAsync(int orderId, string vippsEvent, CancellationToken ct);

        /// <summary>The admin sent the buyer a new payment link. <paramref name="token"/> is the secret in the link.</summary>
        Task PaymentLinkSentAsync(int orderId, string token, CancellationToken ct);

        /// <summary>The payment link ran out without a payment. Only the owner hears.</summary>
        Task PaymentLinkExpiredAsync(int orderId, CancellationToken ct);
    }

    /// <summary>Emails the buyer and the owner about an order. A failed send is logged and never thrown.</summary>
    public class OrderNotifier(ApplicationDbContext db, IEmailSender email, OrderConfirmation confirmation, IConfiguration config, ILogger<OrderNotifier> log) : IOrderNotifier
    {
        public async Task OrderReservedAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            // A confirmation that fails here is sent again by OrderConfirmationRetryService. The owner hears once.
            await confirmation.TrySendAsync(order.Id, order.ConfirmationAttempts, ct);
            await SendAsync(OrderEmails.OwnerNewOrder(order, settings, config), order, ct);
        }

        public async Task OrderCancelledAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerCancelled(order, settings), order, ct);
        }

        public async Task OrderRefundedAsync(int orderId, int amountInOre, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerRefunded(order, amountInOre, settings), order, ct);
        }

        public async Task OrderShippedAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerShipped(order, settings), order, ct);
        }

        public async Task ReservationEndedAsync(int orderId, string vippsEvent, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.OwnerReservationEnded(order, vippsEvent, settings, config), order, ct);
            await SendAsync(OrderEmails.BuyerReservationEnded(order, settings), order, ct);
        }

        public async Task PaymentLinkSentAsync(int orderId, string token, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerPaymentLink(order, SiteLinks.ShopPay(config, order.Locale, token), settings), order, ct);
        }

        public async Task PaymentLinkExpiredAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.OwnerPaymentLinkExpired(order, settings, config), order, ct);
        }

        private async Task<(Models.Shop.Order? Order, AppSettings Settings)> LoadAsync(int orderId, CancellationToken ct)
        {
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null)
                log.LogWarning("No order {OrderId} to send an email about", orderId);

            var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(a => a.Id == 1, ct) ?? new AppSettings();
            return (order, settings);
        }

        private async Task SendAsync(EmailMessage message, Models.Shop.Order order, CancellationToken ct)
        {
            try
            {
                await email.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Could not send {Subject} for order {Reference}", message.Subject, order.Reference);
            }
        }
    }
}
