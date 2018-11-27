using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Searcher
{
    public class SearcherControl : VisualElement
    {
        // Window constants.
        private const string k_WindowTitleLabel = "windowTitleLabel";
        private const string k_WindowDetailsPanel = "windowDetailsVisualContainer";
        private const string k_WindowResultsScrollViewName = "windowResultsScrollView";
        private const string k_WindowSearchTextFieldName = "searchBox";
        private const string k_WindowAutoCompleteLabelName = "autoCompleteLabel";
        private const string k_WindowSearchIconName = "searchIcon";
        private const string k_WindowResizerName = "windowResizer";
        private const int k_TabCharacter = 9;

        private Label m_AutoCompleteLabel;

        private IEnumerable<SearcherItem> m_Results;
        private List<SearcherItem> m_VisibleResults;
        private HashSet<SearcherItem> m_ExpandedResults;
        private Searcher m_Searcher;

        private string m_SuggestedTerm;
        private string m_Text;

        private Action<SearcherItem> m_SelectionCallback;
        private Action<AnalyticsEvent> m_AnalyticsDataCallback;

        internal Label m_TitleLabel { get; }
        internal ListView m_ListView { get; }
        internal TextField m_SearchTextField { get; }
        internal VisualElement m_DetailsPanel { get; }
        internal VisualElement m_Resizer { get; }

        public SearcherControl()
        {
            // Load window template.
            var windowUxmlTemplate = Resources.Load<VisualTreeAsset>("SearcherWindow");

            // Clone Window Template.
            var windowRootVisualElement = windowUxmlTemplate.CloneTree();
            windowRootVisualElement.AddToClassList("content");

            windowRootVisualElement.StretchToParentSize();

            // Add Window VisualElement to window's RootVisualContainer
            Add(windowRootVisualElement);

            m_VisibleResults = new List<SearcherItem>();
            m_ExpandedResults = new HashSet<SearcherItem>();

            m_ListView = this.Q<ListView>(k_WindowResultsScrollViewName);

            if (m_ListView != null)
            {
                m_ListView.bindItem = Bind;
                m_ListView.RegisterCallback<KeyDownEvent>(OnResultsScrollViewKeyDown);
                m_ListView.onItemChosen += obj => { m_SelectionCallback((SearcherItem)obj); };
                m_ListView.onSelectionChanged += selectedObjects =>
                {
                    if (!m_Searcher.adapter.hasDetailsPanel)
                        return;

                    if (selectedObjects.Count > 0)
                        m_Searcher.adapter.DisplaySelectionDetails(m_DetailsPanel, (SearcherItem)selectedObjects[0]);
                    else
                        m_Searcher.adapter.DisplayNoSelectionDetails(m_DetailsPanel);
                };

                m_ListView.focusable = true;
                m_ListView.tabIndex = 1;
            }

            m_DetailsPanel = this.Q(k_WindowDetailsPanel);

            m_TitleLabel = this.Q<Label>(k_WindowTitleLabel);

            m_SearchTextField = this.Q<TextField>(k_WindowSearchTextFieldName);
            if (m_SearchTextField != null)
            {
                m_SearchTextField.focusable = true;
                m_SearchTextField.RegisterCallback<InputEvent>(OnSearchTextFieldTextChanged);
                m_SearchTextField.Q("unity-text-input").RegisterCallback<KeyDownEvent>(OnSearchTextFieldKeyDown);
            }

            m_AutoCompleteLabel = this.Q<Label>(k_WindowAutoCompleteLabelName);

            m_Resizer = this.Q(k_WindowResizerName);

            RegisterCallback<AttachToPanelEvent>(OnEnterPanel);
            RegisterCallback<DetachFromPanelEvent>(OnLeavePanel);

            // TODO: HACK - ListView's scroll view steals focus using the scheduler.
            EditorApplication.update += HackDueToListViewScrollViewStealingFocus;

            style.flexGrow = 1;
        }

        private void HackDueToListViewScrollViewStealingFocus()
        {
            m_SearchTextField.Focus();
            EditorApplication.update -= HackDueToListViewScrollViewStealingFocus;
        }

        private void OnEnterPanel(AttachToPanelEvent e)
        {
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private void OnLeavePanel(DetachFromPanelEvent e)
        {
            UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                CancelSearch();
            }
        }

        private void CancelSearch()
        {
            m_SelectionCallback(null);
            m_AnalyticsDataCallback?.Invoke(new AnalyticsEvent(AnalyticsEvent.EventType.Cancelled, m_SearchTextField.value));
        }

        public void Setup(Searcher searcher, Action<SearcherItem> selectionCallback, Action<AnalyticsEvent> analyticsDataCallback)
        {
            m_Searcher = searcher;
            m_SelectionCallback = selectionCallback;
            m_AnalyticsDataCallback = analyticsDataCallback;

            if (m_Searcher.adapter.hasDetailsPanel)
            {
                m_Searcher.adapter.InitDetailsPanel(m_DetailsPanel);
                m_DetailsPanel.RemoveFromClassList("hidden");
            }
            else
            {
                m_DetailsPanel.AddToClassList("hidden");
            }

            m_TitleLabel.text = m_Searcher.adapter.title;
            if (String.IsNullOrEmpty(m_TitleLabel.text))
            {
                m_TitleLabel.parent.style.visibility = Visibility.Hidden;
                m_TitleLabel.parent.style.position = Position.Absolute;
            }

            Refresh();
        }

        private void Refresh()
        {
            var query = m_SearchTextField.text;

            m_Results = m_Searcher.Search(query);
            GenerateVisibleResults();

            // The first item in the results is always the highest scored item.
            // We want to scroll to and select this item.
            int visibleIndex = -1;
            m_SuggestedTerm = String.Empty;
            if (m_Results.Any())
            {
                SearcherItem scrollToItem = m_Results.First();
                visibleIndex = m_VisibleResults.IndexOf(scrollToItem);

                var cursorIndex = m_SearchTextField.cursorIndex;

                if (query.Length > 0)
                {
                    string[] strings = scrollToItem.name.ToLowerInvariant().Split(' ');
                    int wordStartIndex = cursorIndex == 0 ? 0 : (query.LastIndexOf(' ', cursorIndex - 1) + 1);
                    string word = query.Substring(wordStartIndex, cursorIndex - wordStartIndex);

                    if (word.Length > 0)
                        for (int i = 0; i < strings.Length; i++)
                        {
                            if (strings[i].StartsWith(word))
                            {
                                m_SuggestedTerm = strings[i];
                                break;
                            }
                        }
                }
            }

            m_ListView.itemsSource = m_VisibleResults;
            m_ListView.makeItem = m_Searcher.adapter.MakeItem;

            m_ListView.Refresh();

            SetSelectedElementInResultsList(visibleIndex);
        }

        private void GenerateVisibleResults()
        {
            if (string.IsNullOrEmpty(m_SearchTextField.text))
            {
                m_ExpandedResults.Clear();
                RemoveChildrenFromResults();
                return;
            }

            RegenerateVisibleResults();
            ExpandAllParents();
        }

        private void ExpandAllParents()
        {
            m_ExpandedResults.Clear();
            foreach (SearcherItem item in m_VisibleResults)
                if (item.hasChildren)
                    m_ExpandedResults.Add(item);
        }

        private void RemoveChildrenFromResults()
        {
            m_VisibleResults.Clear();
            var parents = new HashSet<SearcherItem>();

            foreach (var item in m_Results.Where(i => !parents.Contains(i)))
            {
                var currentParent = item;

                while (true)
                {
                    if (currentParent.parent == null)
                    {
                        if (parents.Contains(currentParent))
                            break;

                        parents.Add(currentParent);
                        m_VisibleResults.Add(currentParent);
                        break;
                    }

                    currentParent = currentParent.parent;
                }
            }

            if (m_Searcher.sortComparison != null)
                m_VisibleResults.Sort(m_Searcher.sortComparison);
        }

        private void RegenerateVisibleResults()
        {
            var idSet = new HashSet<SearcherItem>();
            m_VisibleResults.Clear();

            foreach (var item in m_Results.Where(item => !idSet.Contains(item)))
            {
                idSet.Add(item);
                m_VisibleResults.Add(item);

                SearcherItem currentParent = item.parent;
                while (currentParent != null)
                {
                    if (!idSet.Contains(currentParent))
                    {
                        idSet.Add(currentParent);
                        m_VisibleResults.Add(currentParent);
                    }

                    currentParent = currentParent.parent;
                }

                AddResultChildren(item, idSet);
            }

            var comparison = m_Searcher.sortComparison ?? ((i1, i2) =>
            {
                int result = i1.database.id - i2.database.id;
                return result != 0 ? result : i1.id - i2.id;
            });
            m_VisibleResults.Sort(comparison);
        }

        private void AddResultChildren(SearcherItem item, HashSet<SearcherItem> idSet)
        {
            if (!item.hasChildren)
                return;

            foreach (var child in item.children)
            {
                if (!idSet.Contains(child))
                {
                    idSet.Add(child);
                    m_VisibleResults.Add(child);
                }

                AddResultChildren(child, idSet);
            }
        }

        private bool HasChildResult(SearcherItem item)
        {
            if (m_Results.Contains(item))
                return true;

            foreach (var child in item.children)
            {
                if (HasChildResult(child))
                    return true;
            }

            return false;
        }

        private ItemExpanderState GetExpanderState(int index)
        {
            SearcherItem item = m_VisibleResults[index];

            foreach (var child in item.children)
            {
                if (!m_VisibleResults.Contains(child) && !HasChildResult(child))
                    continue;

                return m_ExpandedResults.Contains(item) ? ItemExpanderState.Expanded : ItemExpanderState.Collapsed;
            }

            return ItemExpanderState.Hidden;
        }

        private void Bind(VisualElement target, int index)
        {
            SearcherItem item = m_VisibleResults[index];
            ItemExpanderState expanderState = GetExpanderState(index);
            VisualElement expander = m_Searcher.adapter.Bind(target, item, expanderState, m_Text);
            expander.RegisterCallback<MouseDownEvent>(ExpandOrCollapse);
        }

        private static void GetItemsToHide(SearcherItem parent, ref HashSet<SearcherItem> itemsToHide)
        {
            if (!parent.hasChildren)
            {
                itemsToHide.Add(parent);
                return;
            }

            foreach (SearcherItem child in parent.children)
            {
                itemsToHide.Add(child);
                GetItemsToHide(child, ref itemsToHide);
            }
        }

        private void HideUnexpandedItems()
        {
            // Hide unexpanded children.
            var itemsToHide = new HashSet<SearcherItem>();
            foreach (SearcherItem item in m_VisibleResults)
            {
                if (m_ExpandedResults.Contains(item))
                    continue;

                if (!item.hasChildren)
                    continue;

                if (itemsToHide.Contains(item))
                    continue;

                // We need to hide its children.
                GetItemsToHide(item, ref itemsToHide);
            }

            foreach (SearcherItem item in itemsToHide)
                m_VisibleResults.Remove(item);
        }

        private void RefreshListViewOn(SearcherItem item)
        {
            // TODO: Call ListView.Refresh() when it is fixed.
            // Need this workaround until then.
            // See: https://fogbugz.unity3d.com/f/cases/1027728/
            // And: https://gitlab.internal.unity3d.com/upm-packages/editor/com.unity.searcher/issues/9

            var scrollView = m_ListView.Q<ScrollView>();
            if (scrollView == null)
                return;

            var scroller = scrollView.Q<Scroller>("VerticalScroller");
            if (scroller == null)
                return;

            float oldValue = scroller.value;
            scroller.value = oldValue + 1.0f;
            scroller.value = oldValue - 1.0f;
            scroller.value = oldValue;
        }

        private void Expand(SearcherItem item)
        {
            m_ExpandedResults.Add(item);

            RegenerateVisibleResults();
            HideUnexpandedItems();

            m_ListView.Refresh();
        }

        private void Collapse(SearcherItem item)
        {
            // if it's already collapsed or not collapsed
            if (!m_ExpandedResults.Remove(item))
            {
                // this case applies for a left arrow key press
                if (item.parent != null)
                    SetSelectedElementInResultsList(m_VisibleResults.IndexOf(item.parent));

                // even if it's a root item and has no parents, do nothing more
                return;
            }

            RegenerateVisibleResults();
            HideUnexpandedItems();

            // TODO: understand what happened
            m_ListView.Refresh();

            // RefreshListViewOn(item);
        }

        private void ExpandOrCollapse(MouseDownEvent evt)
        {
            var expanderLabel = evt.target as VisualElement;
            if (expanderLabel == null)
                return;

            VisualElement itemElement = expanderLabel.GetFirstAncestorOfType<TemplateContainer>();
            if (itemElement == null)
                return;

            var item = itemElement.userData as SearcherItem;
            if (item == null || !item.hasChildren || (!expanderLabel.ClassListContains("Expanded") && !expanderLabel.ClassListContains("Collapsed")))
                return;

            if (!m_ExpandedResults.Contains(item))
                Expand(item);
            else
                Collapse(item);

            evt.StopImmediatePropagation();
        }

        private void OnSearchTextFieldTextChanged(InputEvent inputEvent)
        {
            var text = inputEvent.newData;

            if (String.Equals(text, m_Text))
                return;

            // This is necessary due to OnTextChanged(...) being called after user inputs that have no impact on the text.
            // Ex: Moving the caret.
            m_Text = text;

            // If backspace is pressed and no text remain, clear the suggestion label.
            if (String.IsNullOrEmpty(text))
            {
                this.Q(k_WindowSearchIconName).RemoveFromClassList("Active");

                // Display the unfiltered results list.
                Refresh();

                m_AutoCompleteLabel.text = String.Empty;
                m_SuggestedTerm = String.Empty;

                SetSelectedElementInResultsList(0);

                return;
            }

            if (!this.Q(k_WindowSearchIconName).ClassListContains("Active"))
                this.Q(k_WindowSearchIconName).AddToClassList("Active");

            Refresh();
            m_SearchTextField.value = m_SearchTextField.text.ToLower();

            // Calculate the start and end indexes of the word being modified (if any).
            var cursorIndex = m_SearchTextField.cursorIndex;

            // search toward the beginning of the string starting at the character before the cursor
            // +1 because we want the char after a space, or 0 if the search fails
            var wordStartIndex = cursorIndex == 0 ? 0 : (text.LastIndexOf(' ', cursorIndex - 1) + 1);

            // search toward the end of the string from the cursor index
            var wordEndIndex = text.IndexOf(' ', cursorIndex);
            if (wordEndIndex == -1) // no space found, assume end of string
                wordEndIndex = text.Length;

            // Clear the suggestion term if the caret is not within a word (both start and end indexes are equal, ex: (space)caret(space))
            // or the user didn't append characters to a word at the end of the query.
            if (wordStartIndex == wordEndIndex || wordEndIndex < text.Length)
            {
                m_AutoCompleteLabel.text = String.Empty;
                m_SuggestedTerm = String.Empty;
                return;
            }

            var word = text.Substring(wordStartIndex, wordEndIndex - wordStartIndex);

            if (!String.IsNullOrEmpty(m_SuggestedTerm))
            {
                text = text.Remove(wordStartIndex, word.Length);
                text = text.Insert(wordStartIndex, m_SuggestedTerm);
                m_AutoCompleteLabel.text = text;
            }
            else
            {
                m_AutoCompleteLabel.text = String.Empty;
            }
        }

        private void OnResultsScrollViewKeyDown(KeyDownEvent keyDownEvent)
        {
            switch (keyDownEvent.keyCode)
            {
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    return;
                default:
                    SetSelectedElementInResultsList(keyDownEvent);
                    break;
            }
        }

        private void OnSearchTextFieldKeyDown(KeyDownEvent keyDownEvent)
        {
            // First, check if we cancelled the search.
            if (keyDownEvent.keyCode == KeyCode.Escape)
            {
                CancelSearch();
                return;
            }

            // For some reason the KeyDown event is raised twice when entering a character.
            // As such, we ignore one of the duplicate event.
            // This workaround was recommended by the Editor team. The cause of the issue relates to how IMGUI works
            // and a fix was not in the works at the moment of this writing.
            if (keyDownEvent.character == k_TabCharacter)
            {
                // Prevent switching focus to another visual element.
                keyDownEvent.PreventDefault();

                return;
            }

            // If Tab is pressed, complete the query with the suggested term.
            if (keyDownEvent.keyCode == KeyCode.Tab)
            {
                // Used to prevent the TAB input from executing it's default behavior. We're hijacking it for auto-completion.
                keyDownEvent.PreventDefault();

                if (!String.IsNullOrEmpty(m_SuggestedTerm))
                {
                    SelectAndReplaceCurrentWord();
                    m_AutoCompleteLabel.text = String.Empty;

                    // TODO: Revisit, we shouldn't need to do this here.
                    m_Text = m_SearchTextField.text;

                    Refresh();

                    m_SuggestedTerm = String.Empty;
                }
            }
            else
            {
                SetSelectedElementInResultsList(keyDownEvent);
            }
        }

        private void SelectAndReplaceCurrentWord()
        {
            var s = m_SearchTextField.value;
            int lastWordIndex = s.LastIndexOf(' ');
            lastWordIndex++;

            string newText = s.Substring(0, lastWordIndex) + m_SuggestedTerm;

            // Wait for SelectRange api to reach trunk
//#if UNITY_2018_3_OR_NEWER
//            m_SearchTextField.value = newText;
//            m_SearchTextField.SelectRange(m_SearchTextField.value.Length, m_SearchTextField.value.Length);
//#else
            // HACK - relies on the textfield moving the caret when being assigned a value and skipping
            // all low surrogate characters
            string magicMoveCursorToEndString = new string('\uDC00', newText.Length);
            m_SearchTextField.value = magicMoveCursorToEndString;
            m_SearchTextField.value = newText;

//#endif
        }

        private void SetSelectedElementInResultsList(KeyDownEvent keyDownEvent)
        {
            if (m_ListView.childCount == 0)
                return;

            int index;
            switch (keyDownEvent.keyCode)
            {
                case KeyCode.Escape:
                    m_SelectionCallback(null);
                    m_AnalyticsDataCallback?.Invoke(new AnalyticsEvent(AnalyticsEvent.EventType.Cancelled, m_SearchTextField.value));
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (m_ListView.selectedIndex != -1)
                    {
                        m_SelectionCallback((SearcherItem)m_ListView.selectedItem);
                        m_AnalyticsDataCallback?.Invoke(new AnalyticsEvent(AnalyticsEvent.EventType.Picked, m_SearchTextField.value));
                    }
                    else
                    {
                        m_SelectionCallback(null);
                        m_AnalyticsDataCallback?.Invoke(new AnalyticsEvent(AnalyticsEvent.EventType.Cancelled, m_SearchTextField.value));
                    }
                    break;
                case KeyCode.LeftArrow:
                    index = m_ListView.selectedIndex;
                    if (index >= 0 && index < m_ListView.itemsSource.Count)
                        Collapse(m_ListView.selectedItem as SearcherItem);
                    break;
                case KeyCode.RightArrow:
                    index = m_ListView.selectedIndex;
                    if (index >= 0 && index < m_ListView.itemsSource.Count)
                        Expand(m_ListView.selectedItem as SearcherItem);
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    index = m_ListView.selectedIndex;
                    if (index >= 0 && index < m_ListView.itemsSource.Count)
                        m_ListView.OnKeyDown(keyDownEvent);
                    break;
            }
        }

        private void SetSelectedElementInResultsList(int selectedIndex)
        {
            int newIndex = selectedIndex >= 0 && selectedIndex < m_VisibleResults.Count ? selectedIndex : -1;
            if (newIndex < 0)
                return;

            m_ListView.selectedIndex = newIndex;
            m_ListView.ScrollToItem(m_ListView.selectedIndex);
        }

        public class AnalyticsEvent
        {
            public enum EventType{ Pending, Picked, Cancelled }
            public readonly EventType eventType;
            public readonly string currentSearchFieldText;
            public AnalyticsEvent(EventType eventType, string currentSearchFieldText)
            {
                this.eventType = eventType;
                this.currentSearchFieldText = currentSearchFieldText;
            }
        }
    }
}
