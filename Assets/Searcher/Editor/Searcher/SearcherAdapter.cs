using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Searcher
{
    public enum ItemExpanderState
    {
        Hidden,
        Collapsed,
        Expanded
    }

    public interface ISearcherAdapter
    {
        VisualElement MakeItem();
        VisualElement Bind(VisualElement target, SearcherItem item, ItemExpanderState expanderState, string text);

        string title { get; }
        bool hasDetailsPanel { get; }
        void DisplaySelectionDetails(VisualElement detailsPanel, SearcherItem o);
        void DisplayNoSelectionDetails(VisualElement detailsPanel);
        void InitDetailsPanel(VisualElement detailsPanel);
    }

    public class SearcherAdapter : ISearcherAdapter
    {
        const string k_EntryName = "smartSearchItem";
        const int k_IndentDepthFactor = 15;

        public readonly VisualTreeAsset defaultItemTemplate;

        private Label m_DetailsLabel;

        public SearcherAdapter(string title)
        {
            this.title = title;
            defaultItemTemplate = Resources.Load<VisualTreeAsset>("SearcherItem");
        }

        public virtual VisualElement MakeItem()
        {
            // Create a visual element hierarchy for this search result.
            var item = defaultItemTemplate.CloneTree();
            return item;
        }

        public virtual VisualElement Bind(VisualElement element, SearcherItem item, ItemExpanderState expanderState, string query)
        {
            var indent = element.Q<VisualElement>("itemIndent");
            indent.style.width = item.depth * k_IndentDepthFactor;

            var expander = element.Q<VisualElement>("itemChildExpander");

            VisualElement icon = expander.Query("expanderIcon").First();

            icon.ClearClassList();
            if (expanderState == ItemExpanderState.Expanded)
            {
                icon.AddToClassList("Expanded");
            }
            else if (expanderState == ItemExpanderState.Collapsed)
            {
                icon.AddToClassList("Collapsed");
            }

            var nameLabelsContainer = element.Q<VisualElement>("labelsContainer");
            nameLabelsContainer.Clear();

            if (string.IsNullOrWhiteSpace(query))
                nameLabelsContainer.Add(new Label(item.name));
            else
                SearcherHighlighter.HighlightTextBasedOnQuery(nameLabelsContainer, text: item.name, query: query);

            element.userData = item;
            element.name = k_EntryName;

            return expander;
        }

        public virtual string title { get; private set; }

        public virtual bool hasDetailsPanel
        {
            get { return true; }
        }

        public virtual void InitDetailsPanel(VisualElement detailsPanel)
        {
            m_DetailsLabel = new Label();
            detailsPanel.Add(m_DetailsLabel);
        }

        public virtual void DisplaySelectionDetails(VisualElement detailsPanel, SearcherItem item)
        {
            if (m_DetailsLabel != null)
                m_DetailsLabel.text = item.help;
        }

        public virtual void DisplayNoSelectionDetails(VisualElement detailsPanel)
        {
            if (m_DetailsLabel != null)
                m_DetailsLabel.text = "No results";
        }
    }
}
