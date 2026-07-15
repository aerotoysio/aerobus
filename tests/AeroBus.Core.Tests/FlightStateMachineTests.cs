using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Tests;

/// <summary>
/// The flight operational (DCS) status transition table — offline, no backend.
/// </summary>
public class FlightStateMachineTests
{
    [Theory]
    [InlineData("Scheduled", "StartBoarding", "Boarding")]
    [InlineData("Boarding", "Depart", "Departed")]
    [InlineData("Scheduled", "Cancel", "Cancelled")]
    [InlineData("Boarding", "Cancel", "Cancelled")]
    public void Valid_transitions_move_to_the_expected_state(string from, string action, string expected)
    {
        Assert.Equal(expected, FlightStateMachine.TryTransition(from, action));
    }

    [Theory]
    [InlineData("Scheduled", "Depart")]      // can't depart before boarding
    [InlineData("Boarding", "StartBoarding")] // already boarding
    [InlineData("Departed", "Cancel")]        // terminal
    [InlineData("Cancelled", "StartBoarding")] // terminal
    [InlineData("Departed", "Depart")]        // terminal
    public void Invalid_transitions_return_null(string from, string action)
    {
        Assert.Null(FlightStateMachine.TryTransition(from, action));
    }

    [Fact]
    public void Null_or_empty_status_is_treated_as_scheduled()
    {
        Assert.Equal("Boarding", FlightStateMachine.TryTransition(null, "StartBoarding"));
        Assert.Equal("Boarding", FlightStateMachine.TryTransition("", "StartBoarding"));
        Assert.Equal("Scheduled", FlightStateMachine.Normalize(null));
    }

    [Fact]
    public void Available_actions_reflect_the_state()
    {
        Assert.Equal(new[] { "StartBoarding", "Cancel" }.OrderBy(x => x),
            FlightStateMachine.GetAvailableActions("Scheduled").OrderBy(x => x));
        Assert.Equal(new[] { "Depart", "Cancel" }.OrderBy(x => x),
            FlightStateMachine.GetAvailableActions("Boarding").OrderBy(x => x));
        Assert.Empty(FlightStateMachine.GetAvailableActions("Departed"));
    }

    [Theory]
    [InlineData("Departed", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Scheduled", false)]
    [InlineData("Boarding", false)]
    public void Terminal_states_are_flagged(string status, bool terminal)
    {
        Assert.Equal(terminal, FlightStateMachine.IsTerminal(status));
    }
}
