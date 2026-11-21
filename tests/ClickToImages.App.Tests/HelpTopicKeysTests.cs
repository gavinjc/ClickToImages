using Bureau.Common.Help;

namespace ClickToImages.App.Tests;

// HelpTopics.Load leaves out a topic whose Title or Body string is missing, so a mistyped key
// would make a topic quietly vanish from Help. This loads the keys against the resw itself.
public class HelpTopicKeysTests
{
    [Fact]
    public void EveryHelpTopic_HasItsTitleAndBodyInTheResw()
    {
        IReadOnlyList<HelpTopic> topics = HelpTopics.Load(
            key => ResourceKeyTests.ReswValues.TryGetValue(key, out string? value) ? value : "",
            HelpTopicKeys.InOrder);

        Assert.Equal(HelpTopicKeys.InOrder.Count, topics.Count);
    }
}
