using System.Net;
using System.Text;
using Aac.Core.Generation;

namespace Aac.Core.Tests;

public class CandidateValidatorTests
{
    [Theory]
    [InlineData("documentary yesterday ending ridiculous", "I watched that documentary yesterday, and the ending was ridiculous.")]
    [InlineData("documentary yesterday ending ridiculous", "Did you see that documentary yesterday? The ending was ridiculous.")]
    [InlineData("not tired", "I'm not tired.")]
    [InlineData("call sarah 3", "Please call Sarah at 3.")]
    public void Faithful_candidates_are_accepted(string keywords, string candidate) =>
        Assert.True(CandidateValidator.Validate(keywords, candidate).Accepted);

    [Fact]
    public void Dropping_a_keyword_is_rejected()
    {
        var r = CandidateValidator.Validate("documentary yesterday ending ridiculous", "I wanted to talk about that documentary from yesterday.");
        Assert.False(r.Accepted);
        Assert.Contains("ending", r.Reason);
    }

    [Theory]
    [InlineData("want pizza", "I don't want pizza.")]
    [InlineData("don't want pizza", "I want pizza.")]
    [InlineData("not tired", "I'm tired.")]
    public void Flipping_negation_is_rejected(string keywords, string candidate)
    {
        var r = CandidateValidator.Validate(keywords, candidate);
        Assert.False(r.Accepted);
        Assert.Equal("changes negation", r.Reason);
    }

    [Fact]
    public void Changing_numbers_is_rejected() =>
        Assert.False(CandidateValidator.Validate("meds 8", "I take my meds at 9.").Accepted);

    [Fact]
    public void Adding_a_name_is_rejected()
    {
        var r = CandidateValidator.Validate("call later", "Call Sarah later.");
        Assert.False(r.Accepted);
        Assert.Contains("Sarah", r.Reason);
    }

    [Fact]
    public void Added_content_words_are_reported_so_the_user_sees_what_was_invented()
    {
        var r = CandidateValidator.Validate("documentary yesterday ending ridiculous", "I watched that documentary yesterday, and the ending was ridiculous.");
        Assert.Equal(new[] { "watched" }, r.AddedWords);
    }

    [Fact]
    public void Word_forms_match()
    {
        Assert.True(CandidateValidator.SameWord("watching", "watched"));
        Assert.True(CandidateValidator.SameWord("ending", "end"));
        Assert.False(CandidateValidator.SameWord("cat", "car"));
    }
}

public class RuleBasedGeneratorTests
{
    [Fact]
    public void Expands_the_documentary_example()
    {
        var c = RuleBasedGenerator.Generate("documentary yesterday ending ridiculous").ToList();
        Assert.Contains("The ending of the documentary yesterday was ridiculous.", c);
        Assert.Contains("Did you see the documentary yesterday? The ending was ridiculous.", c);
    }

    [Theory]
    [InlineData("documentary yesterday ending ridiculous")]
    [InlineData("not tired")]
    [InlineData("tired")]
    [InlineData("want water")]
    [InlineData("don't want pizza")]
    [InlineData("where remote")]
    [InlineData("game last night amazing")]
    [InlineData("movie boring")]
    [InlineData("need help bathroom")]
    public void Every_offline_candidate_passes_validation(string keywords)
    {
        var candidates = RuleBasedGenerator.Generate(keywords).ToList();
        Assert.NotEmpty(candidates);
        foreach (var c in candidates)
        {
            var v = CandidateValidator.Validate(keywords, c);
            Assert.True(v.Accepted, $"'{c}' rejected: {v.Reason}");
        }
    }

    [Fact]
    public void Negation_is_preserved()
    {
        Assert.All(RuleBasedGenerator.Generate("not tired"), c => Assert.Contains("not", c));
        Assert.All(RuleBasedGenerator.Generate("don't want pizza"), c => Assert.Contains("don't", c));
    }

    [Fact]
    public void Output_is_deterministic() =>
        Assert.Equal(RuleBasedGenerator.Generate("movie boring").ToList(), RuleBasedGenerator.Generate("movie boring").ToList());

    [Fact]
    public void Tidy_capitalises_and_punctuates() =>
        Assert.Equal("I think i'm late.".Replace("i'm", "I'm"), TextTidy.AsSentence("i think i'm late"));
}

public class LocalLlmGeneratorTests
{
    private sealed class StubHandler(string body, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastRequest { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("https://api.example.com/v1/chat/completions")]
    [InlineData("http://192.168.1.20:11434/v1/chat/completions")]
    public void Refuses_endpoints_that_are_not_on_this_computer(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new LocalLlmGenerator(new HttpClient(), endpoint, "model"));

    [Fact]
    public async Task Parses_json_array_reply()
    {
        var reply = """{"choices":[{"message":{"content":"```json\n[\"I watched it.\", \"Did you see it?\"]\n```"}}]}""";
        var handler = new StubHandler(reply);
        var g = new LocalLlmGenerator(new HttpClient(handler), "http://localhost:11434/v1/chat/completions", "small-model");
        var result = await g.GenerateAsync(new GenerationRequest("documentary", Array.Empty<string>(), 3), CancellationToken.None);
        Assert.Equal(new[] { "I watched it.", "Did you see it?" }, result);
        Assert.Contains("documentary", handler.LastRequest);
        Assert.Contains("small-model", handler.LastRequest);
    }

    [Fact]
    public void Falls_back_to_numbered_lines() =>
        Assert.Equal(new[] { "First one.", "Second one." }, LocalLlmGenerator.ParseCandidates("1. First one.\n2) \"Second one.\"\n").ToList());
}
