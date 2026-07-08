namespace AeroBus.Core.Repositories.Order
{
    /// <summary>
    /// Defines valid order status transitions for the AeroBus airline retailing
    /// system. Ported verbatim from ooms Business.Order.OrderStateMachine — this
    /// is the HARD gate: order status only ever changes through
    /// <see cref="TryTransition"/>, regardless of any RuleForge policy hook.
    ///
    /// States: Pending → Confirmed → Fulfilled → CheckedIn → Boarded → Flown → Closed
    /// Terminal: Cancelled, Refunded
    /// </summary>
    public static class OrderStateMachine
    {
        public static class Status
        {
            public const string Pending = "Pending";
            public const string Confirmed = "Confirmed";
            public const string Fulfilled = "Fulfilled";
            public const string CheckedIn = "CheckedIn";
            public const string Boarded = "Boarded";
            public const string Flown = "Flown";
            public const string Closed = "Closed";
            public const string Cancelled = "Cancelled";
            public const string Refunded = "Refunded";
        }

        public static class Action
        {
            public const string Confirm = "Confirm";
            public const string Fulfil = "Fulfil";
            public const string CheckIn = "CheckIn";
            public const string Board = "Board";
            public const string MarkFlown = "MarkFlown";
            public const string Close = "Close";
            public const string Cancel = "Cancel";
            public const string Refund = "Refund";
        }

        private static readonly Dictionary<(string CurrentStatus, string Action), string> Transitions = new()
        {
            // Happy path
            { (Status.Pending, Action.Confirm), Status.Confirmed },
            { (Status.Confirmed, Action.Fulfil), Status.Fulfilled },
            { (Status.Fulfilled, Action.CheckIn), Status.CheckedIn },
            { (Status.CheckedIn, Action.Board), Status.Boarded },
            { (Status.Boarded, Action.MarkFlown), Status.Flown },
            { (Status.Flown, Action.Close), Status.Closed },

            // Cancellation (allowed from Pending, Confirmed, Fulfilled)
            { (Status.Pending, Action.Cancel), Status.Cancelled },
            { (Status.Confirmed, Action.Cancel), Status.Cancelled },
            { (Status.Fulfilled, Action.Cancel), Status.Cancelled },

            // Refund (from Cancelled only)
            { (Status.Cancelled, Action.Refund), Status.Refunded },
        };

        /// <summary>
        /// Attempts a state transition. Returns the new status or null if the
        /// transition is not allowed.
        /// </summary>
        public static string? TryTransition(string currentStatus, string action) =>
            Transitions.TryGetValue((currentStatus, action), out var newStatus) ? newStatus : null;

        /// <summary>Returns all valid actions for the given status.</summary>
        public static IReadOnlyList<string> GetAvailableActions(string currentStatus) =>
            Transitions
                .Where(kvp => kvp.Key.CurrentStatus == currentStatus)
                .Select(kvp => kvp.Key.Action)
                .ToList();

        /// <summary>Checks if a status is terminal (no further transitions possible).</summary>
        public static bool IsTerminal(string status) =>
            status == Status.Closed || status == Status.Refunded;
    }
}
