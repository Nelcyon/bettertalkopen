namespace Aac.Core.Prediction;

/// <summary>
/// Synthetic seed data for the offline predictor. Written for this prototype; it contains no real
/// conversations. Replace or extend with the user's own vocabulary once learning is turned on.
/// </summary>
internal static class SeedCorpus
{
    public static readonly string[] Sentences =
    {
        "I need help", "I need to reposition", "I need some water", "I need to go to the bathroom",
        "I need my glasses", "I need a break", "I need you to call the nurse", "I need my medication",
        "I want to watch a movie", "I want to watch the game", "I want to play a game", "I want to go outside",
        "I want to go to bed", "I want to talk about something", "I want some coffee", "I want to rest",
        "I think that is a good idea", "I think it was ridiculous", "I think we should wait", "I think so",
        "I don't think so", "I don't know", "I don't want that", "I don't like it", "I don't need anything right now",
        "I like it", "I love you", "I love this song", "I am tired", "I am hungry", "I am thirsty", "I am cold",
        "I am hot", "I am uncomfortable", "I am in pain", "I am fine", "I am okay", "I am happy", "I am bored",
        "I'm tired", "I'm fine", "I'm okay", "I'm cold", "I'm hot", "I'm uncomfortable", "I'm sorry",
        "can you help me", "can you move my arm", "can you scratch my nose", "can you adjust my head",
        "can you turn on the tv", "can you turn off the light", "can you turn up the volume",
        "can you turn down the volume", "can you open the window", "can you close the door",
        "can you call my wife", "can you come here", "can you wait a moment", "can you repeat that",
        "please repeat that", "please wait", "please stop", "please come here", "please give me a moment",
        "thank you", "thank you so much", "thanks for coming", "how are you", "how was your day",
        "how was work", "what time is it", "what is for dinner", "what did the doctor say", "what do you think",
        "what are you doing", "where are you going", "when are you coming back", "who is coming today",
        "did you see the game", "did you watch the documentary", "did you hear about that",
        "the ending was ridiculous", "the movie was great", "the game was amazing", "the show was boring",
        "that was funny", "that is not what I meant", "that is right", "that is wrong", "that was a good movie",
        "let me think", "give me a moment", "I will tell you later", "tell me about your day",
        "I watched a documentary yesterday", "we should watch it together", "turn on the computer",
        "the computer is not working", "the eye tracker is not working", "my head is sore", "my legs hurt",
        "my back hurts", "my mouth is dry", "it is too loud", "it is too bright", "it is too cold in here",
        "it is too hot in here", "good morning", "good night", "see you later", "have a good day",
    };

    public static readonly string[] CommonWords =
    {
        "the", "a", "and", "to", "of", "it", "is", "in", "you", "that", "for", "on", "with", "was", "this",
        "be", "are", "not", "have", "but", "what", "can", "we", "my", "me", "do", "so", "if", "just", "about",
        "yes", "no", "please", "help", "wait", "stop", "more", "now", "later", "today", "tomorrow", "yesterday",
        "movie", "game", "show", "music", "song", "book", "news", "weather", "food", "drink", "water",
        "coffee", "tea", "dinner", "lunch", "breakfast", "bed", "chair", "wheelchair", "head", "leg", "arm",
        "computer", "phone", "tv", "remote", "volume", "light", "window", "door", "doctor", "nurse",
        "family", "friend", "wife", "husband", "son", "daughter", "brother", "sister", "mom", "dad",
        "good", "bad", "great", "funny", "boring", "ridiculous", "amazing", "terrible", "interesting",
        "tired", "hungry", "thirsty", "cold", "hot", "pain", "itchy", "uncomfortable", "comfortable",
        "think", "want", "need", "like", "love", "know", "watch", "play", "see", "go", "come", "call",
        "documentary", "ending", "episode", "season", "team", "score", "level", "controller", "quadstick",
    };
}
