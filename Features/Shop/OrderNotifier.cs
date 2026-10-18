using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Features.Shop
{
    public interface IOrderNotifier
    {
        Task OrderReservedAsync(int orderId, CancellationToken ct);
        Task OrderCancelledAsync(int orderId, CancellationToken ct);
        Task OrderRefundedAsync(int orderId, CancellationToken ct);
    }

    /// <summary>Emails the buyer and the owner about an order. A failed send is logged and never thrown.</summary>
    public class OrderNotifier(ApplicationDbContext db, IEmailSender email, IConfiguration config, ILogger<OrderNotifier> log) : IOrderNotifier
    {
        public async Task OrderReservedAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerConfirmation(order, settings, config), order, ct);
            await SendAsync(OrderEmails.OwnerNewOrder(order, settings, config), order, ct);
        }

        public async Task OrderCancelledAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerCancelled(order, settings), order, ct);
        }

        public async Task OrderRefundedAsync(int orderId, CancellationToken ct)
        {
            var (order, settings) = await LoadAsync(orderId, ct);
            if (order == null) return;

            await SendAsync(OrderEmails.BuyerRefunded(order, settings), order, ct);
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
