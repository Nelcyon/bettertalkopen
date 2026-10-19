using Aac.Core.Board;
using Aac.Core.Prediction;

namespace Aac.Core.Tests;

public class WordPredictorTests
{
    [Fact]
    public void Completes_a_partial_word()
    {
        var p = WordPredictor.CreateDefault();
        Assert.Contains("documentary", p.Predict("the doc"));
    }

    [Fact]
    public void Predicts_the_next_word_from_context()
    {
        var p = WordPredictor.CreateDefault();
        var next = p.Predict("i ");
        Assert.Contains("need", next);
        Assert.Contains("want", next);
    }

    [Fact]
    public void Learning_promotes_the_users_own_words()
    {
        var p = WordPredictor.CreateDefault();
        Assert.DoesNotContain("zelda", p.Predict("z"));
        p.Learn("I want to play zelda tonight");
        Assert.Equal("zelda", p.Predict("z")[0]);
        Assert.Equal("zelda", p.Predict("play ")[0]);
    }

    [Fact]
    public void Clearing_personal_data_forgets_learned_words()
    {
        var p = WordPredictor.CreateDefault();
        p.Learn("zelda");
        p.ClearPersonal();
        Assert.DoesNotContain("zelda", p.Predict("z"));
    }

    [Theory]
    [InlineData("", "<s>", "")]
    [InlineData("i wa", "i", "wa")]
    [InlineData("i want ", "want", "")]
    [InlineData("Yes. ", "<s>", "")]
    public void Splits_text_for_prediction(string text, string previous, string partial) =>
        Assert.Equal((previous, partial), WordPredictor.SplitForPrediction(text));
}

public class GridNavigatorTests
{
    private static readonly int[] Rows = { 7, 5, 7 };

    [Fact]
    public void Edges_stop_without_wrap()
    {
        Assert.Equal((0, 0), GridNavigator.Move(Rows, 0, 0, 0, -1, wrap: false));
        Assert.Equal((0, 0), GridNavigator.Move(Rows, 0, 0, -1, 0, wrap: false));
        Assert.Equal((2, 6), GridNavigator.Move(Rows, 2, 6, 0, 1, wrap: false));
    }

    [Fact]
    public void Edges_wrap_when_enabled()
    {
        Assert.Equal((0, 6), GridNavigator.Move(Rows, 0, 0, 0, -1, wrap: true));
        Assert.Equal((2, 0), GridNavigator.Move(Rows, 0, 0, -1, 0, wrap: true));
    }

    [Fact]
    public void Vertical_moves_account_for_wide_cells()
    {
        // [1][  5  ][1] under seven equal cells: the wide middle cell sits under columns 1–5.
        IReadOnlyList<IReadOnlyList<double>> rows = new[] { new double[] { 1, 1, 1, 1, 1, 1, 1 }, new double[] { 1, 5, 1 } };
        Assert.Equal((1, 1), GridNavigator.Move(rows, 0, 3, 1, 0, false));
        Assert.Equal((1, 0), GridNavigator.Move(rows, 0, 0, 1, 0, false));
        Assert.Equal((1, 2), GridNavigator.Move(rows, 0, 6, 1, 0, false));
        Assert.Equal((0, 3), GridNavigator.Move(rows, 1, 1, -1, 0, false));
    }

    [Fact]
    public void Vertical_moves_keep_the_visual_position()
    {
        // centre of a 7-wide row maps to centre of a 5-wide row and back
        Assert.Equal((1, 2), GridNavigator.Move(Rows, 0, 3, 1, 0, false));
        Assert.Equal((2, 3), GridNavigator.Move(Rows, 1, 2, 1, 0, false));
        Assert.Equal((1, 4), GridNavigator.Move(Rows, 0, 6, 1, 0, false));
    }
}

public class DictionaryPredictionTests
{
    private static readonly WordPredictor P = WordPredictor.CreateDefault();

    [Fact]
    public void Ships_a_large_dictionary() => Assert.True(P.VocabularySize > 50_000);

    [Theory]
    [InlineData("fo", "four")]
    [InlineData("four", "four")]
    [InlineData("four", "fourteen")]
    [InlineData("fort", "forty")]
    [InlineData("I want to watch the hock", "hockey")]
    public void Completes_common_words(string typed, string expected) =>
        Assert.Contains(expected, P.Predict(typed));

    [Fact]
    public void Prediction_is_fast_enough_for_every_keystroke()
    {
        P.Predict("warm");
        // Best of several batches, so other tests running in parallel don't make this flaky.
        var best = long.MaxValue;
        for (var batch = 0; batch < 5; batch++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 40; i++) P.Predict("i think the c");
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }
        Assert.True(best < 400, $"{best} ms for 40 predictions");
    }
}

public class EverydaySentenceTests
{
    [Theory]
    [InlineData("water please", "I need water, please.")]
    [InlineData("blanket", "Can I have some blanket?")]
    [InlineData("call nurse", "Can you call the nurse?")]
    [InlineData("tv remote", "Can you get me the tv remote?")]
    public void Plain_words_get_everyday_sentences_that_pass_the_checks(string keywords, string expected)
    {
        var options = Aac.Core.Generation.RuleBasedGenerator.Generate(keywords).ToList();
        Assert.Contains(expected, options);
        Assert.True(options.Count >= 3);
        Assert.All(options, o => Assert.True(Aac.Core.Generation.CandidateValidator.Validate(keywords, o).Accepted, o));
    }

    [Fact]
    public void A_negated_thing_stays_negated()
    {
        var options = Aac.Core.Generation.RuleBasedGenerator.Generate("not coffee").ToList();
        Assert.Contains("I don't want coffee.", options);
        Assert.All(options, o => Assert.True(Aac.Core.Generation.CandidateValidator.Validate("not coffee", o).Accepted, o));
    }
}
