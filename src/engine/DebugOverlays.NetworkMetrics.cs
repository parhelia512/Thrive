using Godot;

/// <summary>
///   Displays network debug.
/// </summary>
public partial class DebugOverlays
{
    [Export]
    public NodePath NetworkMetricsTextPath = null!;

    private Label networkMetricsText = null!;

    private NetworkManager.PingPongData pingPong;

    private int unackedInputsCount;

    public bool NetworkMetricsVisible
    {
        get => networkMetrics.Visible;
        private set
        {
            if (performanceMetricsCheckBox.Pressed == value)
                return;

            performanceMetricsCheckBox.Pressed = value;
        }
    }

    public void ReportPingPong(NetworkManager.PingPongData pingPong)
    {
        this.pingPong = pingPong;
    }

    public void ReportUnackedInputs(int count)
    {
        unackedInputsCount = count;
    }

    private void UpdateNetworkMetrics(float delta)
    {
        networkMetricsText.Text = $"Average RTT: {pingPong.AverageRoundTripTime}\nDelta RTT: {pingPong.DeltaRoundTripTime}\nEstimated Time Offset: {pingPong.EstimatedTimeOffset}\nPacket Lost: {pingPong.PacketLost}\nUnacknowledged Inputs: {unackedInputsCount}";
    }
}
