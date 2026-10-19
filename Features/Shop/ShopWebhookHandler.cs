using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Applies Vipps ePayment events to orders and their builds. An order only moves forward: Paid, Refunded,
    /// Failed and Cancelled are final, except that a Paid order can be refunded. A build is only released or
    /// marked sold while the order still holds it, so an admin's own change to it always stays.
    /// The changes are saved by the library endpoint together with the processed marker.
    /// </summary>
    public class ShopWebhookHandler(
        ApplicationDbContext db,
        IVippsClient vipps,
        IOrderNotifier notifier,
        ILogger<ShopWebhookHandler> log) : IVippsPaymentWebhookHandler
    {
        public async Task<VippsWebhookOutcome> HandleAsync(VippsWebhookDelivery<VippsPaymentEvent> delivery, CancellationToken cancellationToken)
        {
            var e = delivery.Event;
            var eventKey = delivery.EventKey;

            var order = await db.Orders.FirstOrDefaultAsync(o => o.Reference == e.Reference, cancellationToken);
            if (order == null)
            {
                log.LogWarning("Shop webhook {EventKey} rejected: unknown reference {Reference}", eventKey, e.Reference);
                return VippsWebhookOutcome.Rejected;
            }

            if (delivery.IsTest != order.IsTest)
            {
                log.LogError("Shop webhook {EventKey} rejected: sent from the {Sent} environment for {Order} order {OrderId}",
                    eventKey, delivery.IsTest ? "test" : "production", order.IsTest ? "test" : "production", order.Id);
                return VippsWebhookOutcome.Rejected;
            }

            if (!e.Success)
            {
                log.LogWarning("Shop webhook {EventKey} rejected: {Name} did not succeed for order {OrderId}", eventKey, e.Name, order.Id);
                return VippsWebhookOutcome.Rejected;
            }

            // AUTHORIZED reserves the whole order. CAPTURED and REFUNDED can each be part of it.
            var amount = e.Amount?.Value;
            var amountFits = e.Name switch
            {
                "AUTHORIZED" => amount == order.TotalInOre,
                "CAPTURED" or "REFUNDED" => amount > 0 && amount <= order.TotalInOre,
                _ => true
            };
            if (!amountFits)
            {
                log.LogError("Shop webhook {EventKey} rejected: {Name} amount {Got} does not fit the total {Total} of order {OrderId}",
                    eventKey, e.Name, amount, order.TotalInOre, order.Id);
                return VippsWebhookOutcome.Rejected;
            }

            // Tracked, so a change to the build is saved together with the order and the processed marker.
            // An order or build changed after this read fails that save, and Vipps sends the event again.
            var build = order.PcBuildId is int buildId ? await db.PcBuilds.FindAsync([buildId], cancellationToken) : null;
            var previous = order.Status;
            var at = e.Timestamp?.UtcDateTime ?? DateTime.UtcNow;
            var refundedNow = 0;

            try
            {
                switch (e.Name)
                {
                    case "AUTHORIZED":
                        if (previous == OrderStatus.Pending)
                        {
                            order.Status = OrderStatus.Reserved;
                            order.AuthorizedAt = at;
                        }
                        break;
                    case "CAPTURED":
                        if (previous is OrderStatus.Pending or OrderStatus.Reserved
                            && !await CaptureAsync(order, build, amount!.Value, at, eventKey, cancellationToken))
                            return VippsWebhookOutcome.Retry;
                        break;
                    case "REFUNDED":
                        if (previous == OrderStatus.Paid)
                        {
                            if (await RefundAsync(order, amount!.Value, at, eventKey, cancellationToken) is not int added)
                                return VippsWebhookOutcome.Retry;
                            refundedNow = added;
                        }
                        break;
                    case "ABORTED":
                    case "EXPIRED":
                    case "TERMINATED":
                        if (previous is OrderStatus.Pending or OrderStatus.Reserved)
                            Fail(order, build, e.Name, eventKey);
                        break;
                    case "CANCELLED":
                        if (previous is OrderStatus.Pending or OrderStatus.Reserved)
                            Cancel(order, build, at, eventKey);
                        break;
                    default:
                        log.LogInformation("Shop webhook {EventKey}: {Name} for order {OrderId} changes nothing", eventKey, e.Name, order.Id);
                        break;
                }
            }
            catch (Exception ex) when (ShopVipps.IsProviderFailure(ex, cancellationToken))
            {
                log.LogWarning(ex, "Shop webhook {EventKey}: could not read the payment of order {OrderId} from Vipps, left for a retry", eventKey, order.Id);
                return VippsWebhookOutcome.Retry;
            }

            if (db.Entry(order).State == EntityState.Modified) order.UpdatedAt = DateTime.UtcNow;

            var orderId = order.Id;
            // Every refund this event recorded gets its email, whether or not it refunded the whole order.
            // A refund the admin already recorded adds nothing here, so it sends nothing.
            if (refundedNow > 0)
            {
                log.LogInformation("Shop webhook {EventKey}: recorded a refund of {Amount} øre on order {OrderId}, the order is {Status}",
                    eventKey, refundedNow, order.Id, order.Status);
                return VippsWebhookOutcome.Applied(ct => notifier.OrderRefundedAsync(orderId, refundedNow, ct));
            }

            if (order.Status == previous) return VippsWebhookOutcome.Applied();

            log.LogInformation("Shop webhook {EventKey}: order {OrderId} {Previous} -> {Status}", eventKey, order.Id, previous, order.Status);

            return order.Status switch
            {
                // AUTHORIZED never arrived, so the buyer and the owner have not heard about the order yet.
                OrderStatus.Paid when previous == OrderStatus.Pending => VippsWebhookOutcome.Applied(ct => notifier.OrderReservedAsync(orderId, ct)),
                OrderStatus.Reserved => VippsWebhookOutcome.Applied(ct => notifier.OrderReservedAsync(orderId, ct)),
                OrderStatus.Cancelled => VippsWebhookOutcome.Applied(ct => notifier.OrderCancelledAsync(orderId, ct)),
                _ => VippsWebhookOutcome.Applied()
            };
        }

        /// <summary>
        /// Vipps confirms the capture: the captured amount comes from the payment, never from the event alone.
        /// A payment that does not show the event's amount yet changes nothing and returns false, so the event is retried.
        /// A full capture pays the order and sells the build. A partial one records what Vipps has captured so far.
        /// </summary>
        private async Task<bool> CaptureAsync(Order order, PcBuild? build, int eventAmount, DateTime at, string eventKey, CancellationToken ct)
        {
            var captured = (await vipps.GetPaymentAsync(order.Reference, order.IsTest, ct)).CapturedAmountInOre;
            if (captured < eventAmount)
            {
                log.LogWarning("Shop webhook {EventKey}: Vipps does not show the capture of {Amount} øre on order {OrderId} yet, it shows {Captured} øre, left for a retry",
                    eventKey, eventAmount, order.Id, captured);
                return false;
            }
            order.CapturedInOre = Math.Clamp(Math.Max(order.CapturedInOre, captured), 0, order.TotalInOre);

            if (order.CapturedInOre < order.TotalInOre)
            {
                log.LogError("Shop webhook {EventKey}: partial capture of {Captured} of {Total} øre on order {OrderId}, the order stays {Status}",
                    eventKey, order.CapturedInOre, order.TotalInOre, order.Id, order.Status);
                return true;
            }

            order.Status = OrderStatus.Paid;
            order.CapturedAt = at;
            if (build != null && !BuildHold.MarkSold(build, order.Id, DateOnly.FromDateTime(DateTime.UtcNow)))
                log.LogWarning("Shop webhook {EventKey}: order {OrderId} is paid but no longer holds build {BuildId}, so the build was not marked sold",
                    eventKey, order.Id, build.Id);
            return true;
        }

        /// <summary>
        /// Records what Vipps has refunded in total. The order is refunded once that reaches the captured total.
        /// The build stays sold. Returns how much this event added, or null when the payment does not show the
        /// event's amount yet, so the event is retried.
        /// </summary>
        private async Task<int?> RefundAsync(Order order, int eventAmount, DateTime at, string eventKey, CancellationToken ct)
        {
            var payment = await vipps.GetPaymentAsync(order.Reference, order.IsTest, ct);
            if (payment.RefundedAmountInOre < eventAmount)
            {
                log.LogWarning("Shop webhook {EventKey}: Vipps does not show the refund of {Amount} øre on order {OrderId} yet, it shows {Refunded} øre, left for a retry",
                    eventKey, eventAmount, order.Id, payment.RefundedAmountInOre);
                return null;
            }
            var added = OrderRefunds.Record(order, payment.RefundedAmountInOre, at);
            if (added == 0)
                log.LogInformation("Shop webhook {EventKey}: order {OrderId} already has the {Refunded} øre Vipps has refunded",
                    eventKey, order.Id, order.RefundedInOre);
            return added;
        }

        private void Fail(Order order, PcBuild? build, string name, string eventKey)
        {
            // The buyer paid part of the order. That needs a person, so the order and the build stay as they are.
            if (order.CapturedInOre > 0)
            {
                log.LogError("Shop webhook {EventKey}: {Name} on order {OrderId} after a capture of {Captured} of {Total} øre, the order stays {Status} and needs manual handling",
                    eventKey, name, order.Id, order.CapturedInOre, order.TotalInOre, order.Status);
                return;
            }

            order.Status = OrderStatus.Failed;
            if (build != null) BuildHold.Release(build, order.Id);
        }

        private void Cancel(Order order, PcBuild? build, DateTime at, string eventKey)
        {
            // The buyer paid part of the order and the rest was released. That needs a person, so the build stays held.
            if (order.CapturedInOre > 0)
            {
                log.LogError("Shop webhook {EventKey}: order {OrderId} was captured for {Captured} of {Total} øre and the rest cancelled, the order stays {Status} and needs manual handling",
                    eventKey, order.Id, order.CapturedInOre, order.TotalInOre, order.Status);
                return;
            }

            // A payment cancelled before the buyer authorized it never reserved anything, so the order fails without an email.
            if (order.Status == OrderStatus.Pending)
            {
                order.Status = OrderStatus.Failed;
            }
            else
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledAt = at;
            }
            if (build != null) BuildHold.Release(build, order.Id);
        }
    }
}
