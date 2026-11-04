using VigiaHud.Core.Recording;

namespace VigiaHud.Core.Tests.Recording;

public sealed class SequenceLabelsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vigiahud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Person_lines_give_the_stretches_in_order_and_comments_are_skipped()
    {
        var labels = SequenceLabels.Parse("""
            # cruzo el salón
            persona 01:05-01:20

            persona 00:12-00:31
            """);

        Assert.Equal(
            [new PersonInterval(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(31)), new PersonInterval(TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(80))],
            labels.People);
        Assert.True(labels.AnyPerson);
    }

    [Fact]
    public void A_file_with_only_comments_says_nobody_was_there()
    {
        var labels = SequenceLabels.Parse("# casa vacía\n# sin nadie\n");

        Assert.False(labels.AnyPerson);
        Assert.False(labels.HasPersonAt(TimeSpan.FromHours(3)));
    }

    [Theory]
    [InlineData("persona 0:12-0:31", 12, 31)]
    [InlineData("PERSONA 00:12 - 00:31", 12, 31)]
    [InlineData("persona 75:10-75:10", 4510, 4510)]
    [InlineData("persona 1:15:10-2:00:00", 4510, 7200)]
    [InlineData("persona 00:12-00:31\r", 12, 31)]
    public void Times_read_with_minutes_or_hours(string line, int start, int end) =>
        Assert.Equal(new PersonInterval(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)), SequenceLabels.Parse(line).People.Single());

    [Theory]
    [InlineData("gato 00:12-00:31", "Línea 2:")]
    [InlineData("persona 00:12", "Línea 2:")]
    [InlineData("persona 00:61-01:00", "Línea 2:")]
    [InlineData("persona 1:61:00-2:00:00", "Línea 2:")]
    [InlineData("persona 00:31-00:12", "Línea 2: el tramo acaba (00:12) antes de empezar (00:31).")]
    public void A_wrong_line_says_which_one(string line, string message)
    {
        var error = Assert.Throws<FormatException>(() => SequenceLabels.Parse($"# primera\n{line}\n"));

        Assert.StartsWith(message, error.Message);
    }

    [Theory]
    [InlineData(11.9, false)]
    [InlineData(12, true)]
    [InlineData(31, true)]
    [InlineData(31.99, true)]
    [InlineData(32, false)]
    public void A_stretch_covers_its_first_and_last_second_whole(double seconds, bool inside) =>
        Assert.Equal(inside, SequenceLabels.Parse("persona 00:12-00:31").HasPersonAt(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(12, "00:12")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(43200, "12:00:00")]
    public void Offsets_are_written_as_they_are_read(int seconds, string text)
    {
        Assert.Equal(text, SequenceLabels.Format(TimeSpan.FromSeconds(seconds)));
        Assert.Equal(TimeSpan.FromSeconds(seconds), SequenceLabels.ParseOffset(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("a:12")]
    [InlineData("00:12:")]
    [InlineData("1:2:3:4")]
    public void Anything_else_is_not_an_offset(string text) =>
        Assert.Throws<FormatException>(() => SequenceLabels.ParseOffset(text));

    [Fact]
    public void The_labels_file_sits_next_to_the_recording_with_the_same_name()
    {
        Directory.CreateDirectory(_directory);
        var recording = Path.Combine(_directory, "20261104-100319Z-salida-1.vseq");

        Assert.Null(SequenceLabels.ReadFor(recording));

        File.WriteAllText(Path.Combine(_directory, "20261104-100319Z-salida-1.txt"), "persona 00:05-00:09\n");
        Assert.True(SequenceLabels.ReadFor(recording)!.HasPersonAt(TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public void A_stretch_prints_as_its_line() =>
        Assert.Equal("persona 00:12-1:00:31", new PersonInterval(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(3631)).ToString());
}
