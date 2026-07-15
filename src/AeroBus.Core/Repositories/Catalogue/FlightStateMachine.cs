namespace AeroBus.Core.Repositories.Catalogue
{
    /// <summary>
    /// Valid operational (DCS) status transitions for a <see cref="Model.Catalogue.Flight"/>.
    /// Mirrors <see cref="Order.OrderStateMachine"/>: a flight's operational status
    /// only ever changes through <see cref="TryTransition"/>. The flight builder
    /// creates flights <c>Scheduled</c> and cancels them <c>Cancelled</c>; the
    /// operational lifecycle adds boarding and departure on top.
    ///
    /// States: Scheduled → Boarding → Departed. Terminal: Departed, Cancelled.
    /// </summary>
    public static class FlightStateMachine
    {
        public static class Status
        {
            public const string Scheduled = "Scheduled";
            public const string Boarding = "Boarding";
            public const string Departed = "Departed";
            public const string Cancelled = "Cancelled";
        }

        public static class Action
        {
            public const string StartBoarding = "StartBoarding";
            public const string Depart = "Depart";
            public const string Cancel = "Cancel";
        }

        private static readonly Dictionary<(string CurrentStatus, string Action), string> Transitions = new()
        {
            { (Status.Scheduled, Action.StartBoarding), Status.Boarding },
            { (Status.Boarding, Action.Depart), Status.Departed },
            { (Status.Scheduled, Action.Cancel), Status.Cancelled },
            { (Status.Boarding, Action.Cancel), Status.Cancelled },
        };

        /// <summary>A freshly built flight may carry a null/empty status; treat it as
        /// <see cref="Status.Scheduled"/> so transitions resolve predictably.</summary>
        public static string Normalize(string? status) =>
            string.IsNullOrWhiteSpace(status) ? Status.Scheduled : status;

        /// <summary>Attempts a transition. Returns the new status, or null if not allowed.</summary>
        public static string? TryTransition(string? currentStatus, string action) =>
            Transitions.TryGetValue((Normalize(currentStatus), action), out var newStatus) ? newStatus : null;

        /// <summary>All valid actions from the given status.</summary>
        public static IReadOnlyList<string> GetAvailableActions(string? currentStatus)
        {
            var current = Normalize(currentStatus);
            return Transitions
                .Where(kvp => kvp.Key.CurrentStatus == current)
                .Select(kvp => kvp.Key.Action)
                .ToList();
        }

        /// <summary>True when no further transitions are possible.</summary>
        public static bool IsTerminal(string? status)
        {
            var s = Normalize(status);
            return s == Status.Departed || s == Status.Cancelled;
        }
    }
}
