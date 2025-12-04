namespace Drasicrhsit.Infrastructure;

public static class AgUiEventMapper
{
    public static string TextMessage(string text) => System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "TextMessage",
        text
    });

    public static string StateSnapshot(string state) => System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "StateSnapshot",
        state
    });

    public static string MessagesSnapshot(IEnumerable<string> messages) => System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "MessagesSnapshot",
        messages
    });
}
