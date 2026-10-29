using VigiaHud.Core.Bot;

namespace VigiaHud.Core.Tests.Bot;

public class BotBrainTests
{
    private const long Owner = 111_222_333;
    private const long Stranger = 999_888_777;
    private static readonly DateTimeOffset At = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly BotBrain _brain = new(Owner);

    [Fact]
    public void Photo_from_the_owner_asks_for_a_photo() =>
        Assert.IsType<BotDecision.SendPhoto>(_brain.Decide(Message(Owner, "/foto")));

    [Fact]
    public void Status_from_the_owner_asks_for_the_status() =>
        Assert.IsType<BotDecision.SendStatus>(_brain.Decide(Message(Owner, "/estado")));

    [Theory]
    [InlineData("/ayuda")]
    [InlineData("/start")]
    public void Help_from_the_owner_is_answered_with_the_list(string text) =>
        Assert.Equal(new BotDecision.Reply(BotTexts.Help), _brain.Decide(Message(Owner, text)));

    [Fact]
    public void Other_text_from_the_owner_points_to_the_help() =>
        Assert.Equal(new BotDecision.Reply(BotTexts.Unknown), _brain.Decide(Message(Owner, "hola")));

    [Fact]
    public void A_message_without_text_from_the_owner_is_ignored_quietly() =>
        Assert.Equal(new BotDecision.Ignore(null), _brain.Decide(Message(Owner, null)));

    [Theory]
    [InlineData("/foto")]
    [InlineData("/estado")]
    [InlineData("/ayuda")]
    [InlineData("hola")]
    [InlineData(null)]
    public void Anything_from_a_stranger_is_ignored_and_logged(string? text)
    {
        var decision = Assert.IsType<BotDecision.Ignore>(_brain.Decide(Message(Stranger, text)));

        Assert.NotNull(decision.LogLine);
        Assert.Contains(Stranger.ToString(System.Globalization.CultureInfo.InvariantCulture), decision.LogLine);
    }

    [Fact]
    public void The_log_line_for_a_stranger_does_not_repeat_what_they_wrote()
    {
        var decision = Assert.IsType<BotDecision.Ignore>(_brain.Decide(Message(Stranger, "secreto 1234")));

        Assert.DoesNotContain("secreto", decision.LogLine);
        Assert.Contains("12 chars", decision.LogLine);
    }

    [Fact]
    public void An_owner_chat_id_is_required() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BotBrain(0));

    [Fact]
    public void A_group_chat_with_a_negative_id_can_be_the_owner()
    {
        var brain = new BotBrain(-100_123);

        Assert.IsType<BotDecision.SendPhoto>(brain.Decide(Message(-100_123, "/foto")));
    }

    private static IncomingMessage Message(long chat, string? text) => new(1, chat, text, At);
}
