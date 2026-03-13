using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class HelpToolViewModelTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var vm = new HelpToolViewModel();

        vm.Id.Should().Be("Help");
        vm.Title.Should().Be("❓ Help");
        vm.CanClose.Should().BeTrue();
        vm.CanPin.Should().BeTrue();
        vm.SearchQuery.Should().BeEmpty();
    }

    [Fact]
    public void AllTopics_ShouldContainBuiltInTopics()
    {
        var vm = new HelpToolViewModel();

        vm.AllTopics.Should().HaveCountGreaterOrEqualTo(10);
        vm.AllTopics.Select(t => t.Title).Should().Contain("Getting Started");
        vm.AllTopics.Select(t => t.Title).Should().Contain("Keyboard Shortcuts");
        vm.AllTopics.Select(t => t.Title).Should().Contain("Editor Features");
        vm.AllTopics.Select(t => t.Title).Should().Contain("Debugging");
    }

    [Fact]
    public void FilteredTopics_ShouldMatchAll_WhenSearchEmpty()
    {
        var vm = new HelpToolViewModel();

        vm.FilteredTopics.Should().HaveCount(vm.AllTopics.Count);
    }

    [Fact]
    public void FilteredTopics_ShouldFilter_WhenSearchSet()
    {
        var vm = new HelpToolViewModel();

        vm.SearchQuery = "debug";

        vm.FilteredTopics.Should().HaveCountGreaterThan(0);
        vm.FilteredTopics.Should().HaveCountLessThan(vm.AllTopics.Count);
        vm.FilteredTopics.Should().Contain(t => t.Title == "Debugging");
    }

    [Fact]
    public void FilteredTopics_ShouldSearchContent()
    {
        var vm = new HelpToolViewModel();

        vm.SearchQuery = "F5";

        vm.FilteredTopics.Should().HaveCountGreaterThan(0);
        // F5 appears in keyboard shortcuts and debugging content
        vm.FilteredTopics.Select(t => t.Title).Should().Contain("Keyboard Shortcuts");
    }

    [Fact]
    public void FilteredTopics_ShouldSearchCategory()
    {
        var vm = new HelpToolViewModel();

        vm.SearchQuery = "Source Control";

        vm.FilteredTopics.Should().HaveCountGreaterThan(0);
        vm.FilteredTopics.Select(t => t.Title).Should().Contain("Git Integration");
    }

    [Fact]
    public void SelectedTopic_ShouldAutoSelectFirst()
    {
        var vm = new HelpToolViewModel();

        vm.SelectedTopic.Should().NotBeNull();
        vm.SelectedTopic.Should().Be(vm.FilteredTopics[0]);
    }

    [Fact]
    public void SelectedTopic_ShouldUpdateWhenFiltered()
    {
        var vm = new HelpToolViewModel();

        vm.SearchQuery = "NuGet";

        vm.SelectedTopic.Should().NotBeNull();
        vm.SelectedTopic!.Title.Should().Be("NuGet Packages");
    }

    [Fact]
    public void ClearSearch_ShouldResetQuery()
    {
        var vm = new HelpToolViewModel();
        vm.SearchQuery = "debug";

        vm.ClearSearchCommand.Execute(null);

        vm.SearchQuery.Should().BeEmpty();
        vm.FilteredTopics.Should().HaveCount(vm.AllTopics.Count);
    }

    [Fact]
    public void SearchQuery_ShouldRaisePropertyChanged()
    {
        var vm = new HelpToolViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SearchQuery") raised = true; };

        vm.SearchQuery = "test";

        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectedTopic_ShouldRaisePropertyChanged()
    {
        var vm = new HelpToolViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SelectedTopic") raised = true; };

        vm.SelectedTopic = vm.AllTopics.Last();

        raised.Should().BeTrue();
    }

    [Fact]
    public void HelpTopic_Record_ShouldHaveCorrectProperties()
    {
        var topic = new HelpTopic("Test Title", "Test Category", "Test Content");

        topic.Title.Should().Be("Test Title");
        topic.Category.Should().Be("Test Category");
        topic.Content.Should().Be("Test Content");
    }

    [Fact]
    public void FilteredTopics_ShouldShowAll_WhenNoMatch()
    {
        var vm = new HelpToolViewModel();

        vm.SearchQuery = "xyznonexistent123";

        vm.FilteredTopics.Should().BeEmpty();
        vm.SelectedTopic.Should().BeNull();
    }

    [Fact]
    public void AllTopics_ShouldHaveNonEmptyContent()
    {
        var vm = new HelpToolViewModel();

        foreach (var topic in vm.AllTopics)
        {
            topic.Title.Should().NotBeNullOrWhiteSpace();
            topic.Category.Should().NotBeNullOrWhiteSpace();
            topic.Content.Should().NotBeNullOrWhiteSpace();
        }
    }
}
