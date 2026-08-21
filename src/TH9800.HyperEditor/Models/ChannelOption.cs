namespace TH9800.HyperEditor.Models;

public sealed record ChannelOption(int Number, string Name, string Frequency)
{
    public string Display => string.IsNullOrWhiteSpace(Name)
        ? $"{Number:000}  Unnamed memory"
        : $"{Number:000}  {Name,-6}  {Frequency}";
}
