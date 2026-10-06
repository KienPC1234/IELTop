using System.Collections.Generic;

namespace IELTop.Services.Learn;

/// <summary>
/// The tutor's built in cue bank: original Part 1 questions, Part 2 cue cards
/// and Part 3 discussion questions, plus read aloud lines for pronunciation
/// drills. Written for this app, so the tutor works fully offline with no
/// licensed text and no language model. A language model is only needed for
/// the free text feedback on answers, never for the cues themselves.
/// </summary>
public static class SpeakingBank
{
    public sealed record Part1Topic(string Topic, IReadOnlyList<string> Questions);

    public sealed record Part2Card(string Title, string Prompt, IReadOnlyList<string> Bullets);

    public sealed record Part3Set(string Theme, IReadOnlyList<string> Questions);

    public static readonly IReadOnlyList<Part1Topic> Part1 = new List<Part1Topic>
    {
        new("Home", new[]
        {
            "Do you live in a house or an apartment?",
            "What is your favourite room in your home?",
            "Is there anything you would change about your home?",
            "Do you prefer living in a city or in the countryside?",
        }),
        new("Work and study", new[]
        {
            "Do you work or are you a student?",
            "What do you enjoy most about your work or studies?",
            "Is there anything difficult about what you do?",
            "What would you like to do in the future?",
        }),
        new("Food", new[]
        {
            "What kind of food do you usually eat?",
            "Can you cook? What can you make?",
            "Do you prefer eating at home or in restaurants?",
            "Has your taste in food changed over the years?",
        }),
        new("Free time", new[]
        {
            "What do you usually do in your free time?",
            "Do you prefer spending free time alone or with others?",
            "How did you spend your free time as a child?",
            "Do you have enough free time at the moment?",
        }),
        new("Travel", new[]
        {
            "Do you enjoy travelling? Why or why not?",
            "Where did you go on your last trip?",
            "Do you prefer short trips or long journeys?",
            "Where would you like to travel next?",
        }),
        new("Weather and seasons", new[]
        {
            "What is the weather usually like where you live?",
            "Which season do you like best?",
            "Does the weather affect your mood?",
            "Would you like to live somewhere with a different climate?",
        }),
        new("Music", new[]
        {
            "Do you enjoy listening to music?",
            "What kind of music do you usually listen to?",
            "Have you ever learned to play an instrument?",
            "Does music help you concentrate?",
        }),
        new("Daily routine", new[]
        {
            "What time do you usually get up?",
            "How do you usually spend your evenings?",
            "Has your routine changed recently?",
            "Would you like to change anything about your daily routine?",
        }),
    };

    public static readonly IReadOnlyList<Part2Card> Part2 = new List<Part2Card>
    {
        new("A useful skill", "Describe a useful skill you learned.",
            new[] { "what the skill is", "how you learned it", "how long it took", "and explain why it is useful." }),
        new("A memorable trip", "Describe a trip you remember well.",
            new[] { "where you went", "who you went with", "what you did there", "and explain why you remember it." }),
        new("A person you admire", "Describe a person you admire.",
            new[] { "who the person is", "how you know them", "what they are like", "and explain why you admire them." }),
        new("A book or film", "Describe a book you read or a film you watched that you enjoyed.",
            new[] { "what it was", "when you read or watched it", "what it was about", "and explain why you enjoyed it." }),
        new("A quiet place", "Describe a quiet place you like to spend time in.",
            new[] { "where it is", "when you go there", "what you do there", "and explain why you like it." }),
        new("A change in your life", "Describe an important change in your life.",
            new[] { "what the change was", "when it happened", "how it affected you", "and explain how you feel about it." }),
    };

    public static readonly IReadOnlyList<Part3Set> Part3 = new List<Part3Set>
    {
        new("Learning skills", new[]
        {
            "What skills do young people most need today?",
            "Is it better to learn alone or with a teacher?",
            "How has technology changed the way people learn?",
        }),
        new("Travel and tourism", new[]
        {
            "Why do you think people enjoy travelling?",
            "Does tourism help or harm local places?",
            "How might travel change in the future?",
        }),
        new("People and role models", new[]
        {
            "What qualities make someone a good role model?",
            "Do famous people have a duty to behave well?",
            "Are role models more important for children or adults?",
        }),
        new("Reading and stories", new[]
        {
            "Do people read as much as they used to?",
            "Are films better than books at telling stories?",
            "Should schools make children read more?",
        }),
        new("Cities and quiet", new[]
        {
            "Why are cities becoming noisier?",
            "Is it important to have quiet places in a city?",
            "How does noise affect health and work?",
        }),
        new("Change", new[]
        {
            "Do most people welcome change or fear it?",
            "What are the biggest changes in daily life today?",
            "Is it easier for young or old people to accept change?",
        }),
    };

    /// <summary>
    /// Read aloud lines, easy first, each rich in a different sound group so
    /// the pronunciation check has something to measure.
    /// </summary>
    public static readonly IReadOnlyList<string> ReadAloud = new List<string>
    {
        "The weather is nice today.",
        "She sells fresh bread every morning.",
        "We watched the red sun set behind the hills.",
        "Three thin trees grow beside the quiet river.",
        "The teacher asked the children to read slowly.",
        "Strong winds shook the windows through the night.",
        "A crowd gathered round the busy market square.",
        "He chose the smooth road through the green valley.",
        "The old bridge joins the northern bank to the town.",
        "Ships with vast white sails crossed the broad ocean.",
        "Knowledge grows when curious minds ask better questions.",
        "Though the journey was rough, the travellers rejoiced together.",
    };

    /// <summary>Recording caps in seconds: Part1 answer, Part2 prep, Part2 talk, Part3 answer.</summary>
    public static int MaxSeconds(string part) => part switch
    {
        "Part2Prep" => 60,
        "Part2" => 120,
        "Part3" => 90,
        _ => 60,
    };
}
