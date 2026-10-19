using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>Records what Vipps has refunded on a paid order. The admin refund and the REFUNDED webhook both use it.</summary>
    public static class OrderRefunds
    {
        /// <summary>
        /// Raises the order's refunded total to what Vipps reports, never above what was captured, and refunds the
        /// order once everything captured is paid back. Returns how much this call added, so only the side that
        /// recorded a refund emails about it. A pending admin refund is cleared once the total moves, since its key
        /// was built from the total before.
        /// </summary>
        public static int Record(Order order, int vippsRefundedInOre, DateTime at)
        {
            var refunded = Math.Clamp(vippsRefundedInOre, 0, order.CapturedInOre);
            if (refunded <= order.RefundedInOre) return 0;

            var added = refunded - order.RefundedInOre;
            order.RefundedInOre = refunded;
            order.PendingRefundKey = null;
            order.PendingRefundInOre = null;
            if (order.RefundedInOre >= order.CapturedInOre)
            {
                order.Status = OrderStatus.Refunded;
                order.RefundedAt = at;
            }
            return added;
        }
    }
}
