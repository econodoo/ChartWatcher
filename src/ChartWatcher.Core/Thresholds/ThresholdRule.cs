namespace ChartWatcher.Core.Thresholds;

public sealed class ThresholdRule
{
    public Guid StickerId { get; set; }
    public ThresholdOperator Operator { get; set; }
    public double Value { get; set; }
    public string ColorBrush { get; set; } = "UpBrush";
    public bool TriggersNarration { get; set; }
    public int Priority { get; set; } = 3;
}

public enum ThresholdOperator
{
    GreaterThan,
    LessThan,
    EqualTo,
    Crosses,
    Flips
}
