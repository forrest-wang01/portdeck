using System.Globalization;
using System.Text.Json.Serialization;
using PortDeck.Localization;

namespace PortDeck.Models;

public sealed class HistoryEvent
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Event { get; set; } = "up";
    public int Port { get; set; }
    public string Protocol { get; set; } = "TCP";
    public int Pid { get; set; }
    public string Process { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    [JsonIgnore]
    public string TimeLabel => Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    [JsonIgnore]
    public string EventLabel => L.Get(Event == "down" ? "HistoryDown" : "HistoryUp");

    [JsonIgnore]
    public string EventTone => Event == "down" ? "down" : "up";

    [JsonIgnore]
    public string Title => $"{Process} · {Port}/{Protocol}";

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(Project)
        ? Address
        : $"{Project}  ·  {Address}";
}
