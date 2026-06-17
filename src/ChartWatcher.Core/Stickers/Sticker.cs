namespace ChartWatcher.Core.Stickers;

public sealed class Selector
{
    public SelectorStrategy Strategy { get; set; }
    public string Expression { get; set; } = string.Empty;
}

public enum SelectorStrategy
{
    ElementId,
    DataAttribute,
    CssPath,
    XPath,
    TextMatch
}

public sealed class SelectorCascade
{
    public List<Selector> Selectors { get; set; } = [];
}

public sealed class Sticker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ValueFormat Format { get; set; } = ValueFormat.Text;
    public string? Unit { get; set; }
    public string? Category { get; set; }
    public string? ParseRule { get; set; }
}

public enum ValueFormat
{
    Number,
    Percent,
    Text,
    Money
}
