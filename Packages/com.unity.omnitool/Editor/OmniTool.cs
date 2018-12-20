﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Omni.Providers;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Omni
{ 
    public delegate Texture2D PreviewHandler(OmniItem item, OmniContext context);
    public delegate void ActionHandler(OmniItem item, OmniContext context);
    public delegate bool EnabledHandler(OmniItem item, OmniContext context);
    public delegate IEnumerable<OmniItem> GetItemsHandler(OmniContext context);

    public class OmniAction
    {
        public OmniAction(string type, GUIContent content)
        {
            this.type = type;
            this.content = content;
            isEnabled = (item, context) => true;
        }

        public OmniAction(string type, string name, Texture2D icon = null, string tooltip = null)
            : this(type, new GUIContent(name, icon, tooltip))
        {
            
        }

        public string type;
        public GUIContent content;
        public ActionHandler handler;
        public EnabledHandler isEnabled;
    }

    public struct OmniItem
    {
        public string id;
        public string label;
        public string description;
        public OmniProvider provider;
    }

    public class OmniFilter
    {
        public class Entry
        {
            public Entry(OmniName name)
            {
                this.name = name;
            }

            public OmniName name;
            public bool isEnabled;
        }

        public class ProviderDesc
        {
            public ProviderDesc(OmniName name)
            {
                entry = new Entry(name);
                categories = new List<Entry>();
                isExpanded = false;
            }

            public Entry entry;
            public bool isExpanded;
            public List<Entry> categories;
        }

        public OmniFilter()
        {
            filteredProviders = new List<OmniProvider>();
            model = new List<ProviderDesc>();
        }

        public bool IsEnabled(string providerId, string subCategory = null)
        {
            var desc = model.Find(pd => pd.entry.name.id == providerId);
            if (desc != null)
            {
                if (subCategory == null)
                {
                    return desc.entry.isEnabled;
                }

                foreach (var cat in desc.categories)
                {
                    if (cat.name.id == subCategory)
                        return cat.isEnabled;
                }
            }

            return false;
        }

        public List<Entry> GetSubCategories(OmniProvider provider)
        {
            var desc = model.Find(pd => pd.entry.name.id == provider.name.id);
            if (desc == null)
            {
                return null;
            }

            return desc.categories;
        }

        public List<OmniProvider> filteredProviders;
        public List<ProviderDesc> model;
    }

    public class OmniName
    {
        public OmniName(string id, string displayName = null)
        {
            this.id = id;
            this.displayName = displayName ?? id;
        }
        public string id;
        public string displayName;
    }

    public class OmniProvider
    {
        public OmniProvider(string id, string displayName = null)
        {
            name = new OmniName(id, displayName);
            actions = new List<OmniAction>();
            fetchItems = (context) => new OmniItem[0];
            generatePreview = (item, context) => null;
            subCategories = new List<OmniName>();
        }

        public static bool MatchSearchGroups(string searchContext, string content)
        {
            return MatchSearchGroups(searchContext, content, out _, out _);
        }

        public static bool MatchSearchGroups(string searchContext, string content, out int startIndex, out int endIndex)
        {
            startIndex = endIndex = -1;
            if (content == null)
                return false;

            if (string.IsNullOrEmpty(searchContext) || searchContext == content)
            {
                startIndex = 0;
                endIndex = content.Length - 1;
                return true;
            }

            // Each search group is space separated
            // Search group must match in order and be complete.
            var searchGroups = searchContext.Split(' ');
            var startSearchIndex = 0;
            foreach (var searchGroup in searchGroups)
            {
                if (searchGroup.Length == 0)
                    continue;

                startSearchIndex = content.IndexOf(searchGroup, startSearchIndex, StringComparison.CurrentCultureIgnoreCase);
                if (startSearchIndex == -1)
                {
                    return false;
                }

                startIndex = startIndex == -1 ? startSearchIndex : startIndex;
                startSearchIndex = endIndex = startSearchIndex + searchGroup.Length - 1;
            }

            return startIndex != -1 && endIndex != -1;
        }

        public OmniName name;
        public PreviewHandler generatePreview;
        public GetItemsHandler fetchItems;
        public List<OmniAction> actions;
        public List<OmniName> subCategories;
    }

    public struct OmniContext
    {
        public string searchText;
        public EditorWindow focusedWindow;
        public List<OmniFilter.Entry> categories;
    }

    public class OmniItemProviderAttribute : Attribute
    {
    }

    public class OmniActionsProviderAttribute : Attribute
    {
    }

    public static class OmniService
    {
        const string k_KFilterPrefKey = "omnitool.filters";
        const string k_KFilterExpandedPrefKey = "omnitool.filters_expanded";
        public static List<OmniProvider> Providers { get; private set; }

        static OmniService()
        {
            Filter = new OmniFilter();
            FetchProviders();
            LoadFilters();
        }

        public static OmniFilter Filter { get; private set; }
        
        public static IEnumerable<OmniItem> GetItems(OmniContext context)
        {
            return Filter.filteredProviders.SelectMany(provider =>
            {
                context.categories = Filter.GetSubCategories(provider);
                return provider.fetchItems(context).Select(item =>
                {
                    item.provider = provider;
                    return item;
                });
            });
        }

        public static void ResetFilter(bool enableAll)
        {
            foreach (var providerDesc in Filter.model)
            {
                SetFilterInternal(enableAll, providerDesc.entry.name.id);
            }
            FilterChanged();
        }

        public static void SetFilter(bool isEnabled, string providerId, string subCategory = null)
        {
            if (SetFilterInternal(isEnabled, providerId, subCategory))
            {
                FilterChanged();
            }
        }

        public static void SetExpanded(bool isExpanded, string providerId)
        {
            var providerDesc = Filter.model.Find(pd => pd.entry.name.id == providerId);
            if (providerDesc != null)
            {
                providerDesc.isExpanded = isExpanded;

                var filtersExpandedStr = string.Join(",", Filter.model.Where(pd => pd.isExpanded).Select(pd => pd.entry.name.id));
                Debug.Log("Save filter expanded: " + filtersExpandedStr);
                EditorPrefs.SetString(k_KFilterExpandedPrefKey, filtersExpandedStr);
            }
        }

        private static void FilterChanged()
        {
            UpdateFilteredProviders();
            SaveFilters();
        }

        private static void UpdateFilteredProviders()
        {
            Filter.filteredProviders = Providers.Where(p => Filter.IsEnabled(p.name.id)).ToList();
        }

        private static bool SetFilterInternal(bool isEnabled, string providerId, string subCategory = null)
        {
            var providerDesc = Filter.model.Find(pd => pd.entry.name.id == providerId);
            if (providerDesc != null)
            {
                if (subCategory == null)
                {
                    providerDesc.entry.isEnabled = isEnabled;
                    foreach (var cat in providerDesc.categories)
                    {
                        cat.isEnabled = isEnabled;
                    }
                }
                else
                {
                    foreach (var cat in providerDesc.categories)
                    {
                        if (cat.name.id == subCategory)
                        {
                            cat.isEnabled = isEnabled;
                            if (isEnabled)
                                providerDesc.entry.isEnabled = true;
                        }
                    }
                }

                return true;
            }

            return false;
        }

        private static void FetchProviders()
        {
            Providers = GetAllMethodsWithAttribute<OmniItemProviderAttribute>()
                .Select(methodInfo => methodInfo.Invoke(null, null) as OmniProvider)
                .Where(provider => provider != null).ToList();

            foreach (var action in GetAllMethodsWithAttribute<OmniActionsProviderAttribute>()
                .SelectMany(methodInfo => methodInfo.Invoke(null, null) as object[]).Where(a => a != null).Cast<OmniAction>())
            {
                var provider = Providers.Find(p => p.name.id == action.type);
                provider?.actions.Add(action);
            }

            foreach (var provider in Providers)
            {
                var providerFilter = new OmniFilter.ProviderDesc(provider.name);
                Filter.model.Add(providerFilter);
                foreach (var subCategory in provider.subCategories)
                {
                    providerFilter.categories.Add(new OmniFilter.Entry(subCategory));
                }
            }
        }

        private static IEnumerable<MethodInfo> GetAllMethodsWithAttribute<T>(BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        {
            Assembly assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "EditorAssemblies");
            var method = managerType.GetMethod("Internal_GetAllMethodsWithAttribute", BindingFlags.NonPublic | BindingFlags.Static);
            var arguments = new object[] { typeof(T), bindingFlags };
            return ((method.Invoke(null, arguments) as object[]) ?? throw new InvalidOperationException()).Cast<MethodInfo>();
        }

        private static void LoadFilters()
        {
            var filtersExpandedStr = EditorPrefs.GetString(k_KFilterExpandedPrefKey, null);
            if (filtersExpandedStr != null)
            {
                var filtersExpanded = filtersExpandedStr.Split(',');
                foreach (var filterExpanded in filtersExpanded)
                {
                    var desc = Filter.model.Find(p => p.entry.name.id == filterExpanded);
                    if (desc != null)
                        desc.isExpanded = true;
                }
            }

            var filtersStr = EditorPrefs.GetString(k_KFilterPrefKey, null);
            Debug.Log("Load filters: " + filtersStr);
            if (filtersStr == null)
            {
                ResetFilter(true);
                return;
            }

            var filters = filtersStr.Split(',');
            foreach (var filterStr in filters)
            {
                var filter = filterStr.Split(':');
                if (filter.Length == 2)
                {
                    SetFilterInternal(true, filter[0], filter[1]);
                }
                else
                {
                    SetFilterInternal(true, filter[0]);
                }
            }
            UpdateFilteredProviders();
        }

        public static string FilterToString()
        {
            var filterStr = new List<string>();
            foreach (var providerDesc in Filter.model)
            {
                if (providerDesc.categories.Count == 0 && providerDesc.entry.isEnabled)
                    filterStr.Add(providerDesc.entry.name.id);
                foreach (var cat in providerDesc.categories)
                {
                    filterStr.Add(providerDesc.entry.name.id + ":" + cat.name.id);
                }
            }

            return string.Join(",", filterStr);
        }

        private static void SaveFilters()
        {
            var filter = FilterToString();
            Debug.Log("Save filters: "  + filter);
            EditorPrefs.SetString(k_KFilterPrefKey, filter);
        }
    }

    internal class FilterWindow : EditorWindow
    {
        static class Styles
        {
            public static float indent = 10f;
            public static Vector2 windowSize = new Vector2(175, 200);
            public static float rowHeight = 15;

            public static readonly GUIStyle filterHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                name = "omni-tool-filter-header",
                // padding = new RectOffset(2, 2, 1, 2),
                margin = new RectOffset(4, 4, 0, 4)
            };

            public static readonly GUIStyle filterEntry = new GUIStyle(EditorStyles.label)
            {
                name = "omni-tool-filter-entry"
            };

            public static readonly GUIStyle filterToggle = new GUIStyle("OL Toggle")
            {
                name = "omni-tool-filter-toggle",
                // padding = new RectOffset(2, 2, 1, 2),
                // margin = new RectOffset(4, 4, 6, 4)
            };

            public static readonly GUIStyle filterExpanded = new GUIStyle("IN Foldout")
            {
                name = "omni-tool-filter-expanded",
                // padding = new RectOffset(2, 2, 1, 2),
                // margin = new RectOffset(4, 4, 6, 4)
            };

            public static float foldoutIndent = filterExpanded.fixedWidth + 6;
        }

        public OmniTool omniTool;

        Vector2 m_ScrollPos;

        public static bool ShowAtPosition(OmniTool omniTool, Rect rect)
        {
            var screenRect = GUIUtility.GUIToScreenRect(rect);
            var filterWindow = ScriptableObject.CreateInstance<FilterWindow>();
            // var filterWindow = GetWindow<FilterWindow>();
            filterWindow.omniTool = omniTool;
            filterWindow.ShowAsDropDown(screenRect, Styles.windowSize);
            return true;
        }

        void OnGUI()
        {
            m_ScrollPos = GUILayout.BeginScrollView(m_ScrollPos);

            GUILayout.Space(Styles.indent);
            foreach (var providerDesc in OmniService.Filter.model)
            {
                DrawSectionHeader(providerDesc);
                if (providerDesc.isExpanded)
                    DrawSubCategories(providerDesc);
            }

            GUILayout.Space(Styles.indent);
            GUILayout.EndScrollView();
        }

        void DrawSectionHeader(OmniFilter.ProviderDesc desc)
        {
            // filterHeader
            GUILayout.BeginHorizontal();

            if (desc.categories.Count > 0)
            {
                EditorGUI.BeginChangeCheck();
                bool isExpanded = GUILayout.Toggle(desc.isExpanded, "", Styles.filterExpanded);
                if (EditorGUI.EndChangeCheck())
                {
                    OmniService.SetExpanded(isExpanded, desc.entry.name.id);
                }
            }
            else
            {
                GUILayout.Space(Styles.foldoutIndent);
            }

            GUILayout.Label(desc.entry.name.displayName, Styles.filterHeader);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            bool isEnabled = GUILayout.Toggle(desc.entry.isEnabled, "", Styles.filterToggle, GUILayout.ExpandWidth(false));
            if (EditorGUI.EndChangeCheck())
            {
                OmniService.SetFilter(isEnabled, desc.entry.name.id);
                omniTool.Refresh();
            }

            GUILayout.EndHorizontal();
        }

        void DrawSubCategories(OmniFilter.ProviderDesc desc)
        {
            foreach (var cat in desc.categories)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(Styles.foldoutIndent + 5);
                GUILayout.Label(cat.name.displayName, Styles.filterEntry);
                GUILayout.FlexibleSpace();

                EditorGUI.BeginChangeCheck();
                bool isEnabled = GUILayout.Toggle(cat.isEnabled, "", Styles.filterToggle);
                if (EditorGUI.EndChangeCheck())
                {
                    OmniService.SetFilter(isEnabled, desc.entry.name.id, cat.name.id);
                    omniTool.Refresh();
                }

                GUILayout.EndHorizontal();
            }
        }
    }

    static class OmniIcon
    {
        public static Texture2D open = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/open.png");
        public static Texture2D @goto = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/goto.png");
        public static Texture2D shortcut = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/shortcut.png");
        public static Texture2D execute = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/execute.png");
        public static Texture2D omnitool = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/omnitool@2x.png");

        static OmniIcon()
        {
            bool hasPro = UnityEditorInternal.InternalEditorUtility.HasPro();
            if (hasPro)
            {
                open = LightenTexture(open);
                @goto = LightenTexture(@goto);
                shortcut = LightenTexture(shortcut);
                execute = LightenTexture(execute);
                omnitool = LightenTexture(omnitool);
            }
        }

        private static Texture2D LightenTexture(Texture2D texture)
        {
            Texture2D outTexture = new Texture2D(texture.width, texture.height);
            var outColorArray = outTexture.GetPixels();

            var colorArray = texture.GetPixels();
            for (var i = 0; i < colorArray.Length; ++i)
                outColorArray[i] = LightenColor(colorArray[i]);

            outTexture.SetPixels(outColorArray);
            outTexture.Apply();

            return outTexture;
        }

        public static Color LightenColor(Color color)
        {
            Color.RGBToHSV(color, out var h, out _, out _);
            var outColor = Color.HSVToRGB((h + 0.5f) % 1, 0f, 0.8f);
            outColor.a = color.a;
            return outColor;
        }
    }

    public class OmniTool : EditorWindow
    {
        public static EditorWindow s_FocusedWindow;

        [SerializeField] private string m_SearchText;
        [SerializeField] private Vector2 m_ScrollPosition;
        [SerializeField] public EditorWindow lastFocusedWindow;
        [SerializeField] private bool m_OmniSearchBoxFocus;
        [SerializeField] private int m_SelectedIndex = -1;
        private List<OmniItem> m_FilteredItems;
        private bool m_FocusSelectedItem = false;
        private Rect m_ScrollViewOffset;

        enum RectVisibility
        {
            None,
            HiddenTop,
            HiddenBottom,
            PartiallyHiddenTop,
            PartiallyHiddenBottom,
            Visible
        }

        static class Styles
        {
            static Styles()
            {
            }
        
            private const int itemRowPadding = 4;
            private const float actionButtonSize = 24f;
            private const float itemPreviewSize = 32f;
            private const int actionButtonMargin = (int)((itemRowHeight - actionButtonSize) / 2f);
            public const float itemRowHeight = itemPreviewSize + itemRowPadding * 2f;

            private static readonly RectOffset marginNone = new RectOffset(0, 0, 0, 0);
            private static readonly RectOffset paddingNone = new RectOffset(0, 0, 0, 0);
            private static readonly RectOffset defaultPadding = new RectOffset(itemRowPadding, itemRowPadding, itemRowPadding, itemRowPadding);

            private static readonly Texture2D debugBackgroundImage = GenerateSolidColorTexture(new Color(1f, 0f, 0f));
            private static readonly Texture2D alternateRowBackgroundImage = GenerateSolidColorTexture(new Color(61/255f, 61/255f, 61/255f));
            private static readonly Texture2D selectedRowBackgroundImage = GenerateSolidColorTexture(new Color(61/255f, 96/255f, 145/255f));
            private static readonly Texture2D selectedHoveredRowBackgroundImage = GenerateSolidColorTexture(new Color(71/255f, 106/255f, 155/255f));
            private static readonly Texture2D hoveredRowBackgroundImage = GenerateSolidColorTexture(new Color(68/255f, 68/255f, 71/255f));
            private static readonly Texture2D buttonPressedBackgroundImage = GenerateSolidColorTexture(new Color(111/255f, 111/255f, 111/255f));
            private static readonly Texture2D buttonHoveredBackgroundImage = GenerateSolidColorTexture(new Color(71/255f, 71/255f, 71/255f));

            public static readonly GUIContent filterButtonContent = new GUIContent("filter");

            public static readonly GUIStyle itemBackground1 = new GUIStyle
            {
                name = "omni-tool-item-background1",
                fixedHeight = itemRowHeight,

                margin = marginNone,
                padding = defaultPadding,

                hover = new GUIStyleState { background = hoveredRowBackgroundImage, scaledBackgrounds = new[] { hoveredRowBackgroundImage } }
            };

            public static readonly GUIStyle itemBackground2 = new GUIStyle(itemBackground1)
            {
                name = "omni-tool-item-background2",
                normal = new GUIStyleState { background = alternateRowBackgroundImage, scaledBackgrounds = new[] { alternateRowBackgroundImage } }
            };

            public static readonly GUIStyle selectedItemBackground = new GUIStyle(itemBackground1)
            {
                name = "omni-tool-item-selected-background",
                normal = new GUIStyleState { background = selectedRowBackgroundImage, scaledBackgrounds = new[] { selectedRowBackgroundImage } },
                hover = new GUIStyleState { background = selectedHoveredRowBackgroundImage, scaledBackgrounds = new[] { selectedHoveredRowBackgroundImage } }
            };

            public static readonly GUIStyle preview = new GUIStyle
            {
                name = "omni-tool-item-preview",
                fixedWidth = itemPreviewSize,
                fixedHeight = itemPreviewSize,

                margin = new RectOffset(2, 2, 2, 2),
                padding = paddingNone
            };

            public static readonly GUIStyle itemLabel = new GUIStyle(EditorStyles.label)
            {
                name = "omni-tool-item-label",

                margin = new RectOffset(4, 4, 6, 2),
                padding = paddingNone
            };

            public static readonly GUIStyle itemDescription = new GUIStyle(EditorStyles.label)
            {
                name = "omni-tool-item-description",

                margin = new RectOffset(4, 4, 1, 4),
                padding = paddingNone,

                fontSize = itemLabel.fontSize - 2,
                fontStyle = FontStyle.Italic
            };

            public static readonly GUIStyle actionButton = new GUIStyle("IconButton")
            {
                name = "omni-tool-action-button",

                fixedWidth = actionButtonSize,
                fixedHeight = actionButtonSize,

                imagePosition = ImagePosition.ImageOnly,

                margin = new RectOffset(4, 4, actionButtonMargin, actionButtonMargin),
                padding = paddingNone,

                active = new GUIStyleState { background = buttonPressedBackgroundImage, scaledBackgrounds = new[] { buttonPressedBackgroundImage } },
                hover = new GUIStyleState { background = buttonHoveredBackgroundImage, scaledBackgrounds = new[] { buttonHoveredBackgroundImage } }
            };

            public static readonly GUIStyle toolbar = new GUIStyle("Toolbar")
            {
                name = "omni-tool-bar",
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 0, 0, 0)
            };

            public static readonly GUIStyle searchField = new GUIStyle(EditorStyles.toolbarSearchField) { name = "omni-tool-search-field" };
            public static readonly GUIStyle searchFieldClear = new GUIStyle("ToolbarSeachCancelButton") { name = "omni-tool-search-field-clear" };
            public static readonly GUIStyle filterButton = new GUIStyle(EditorStyles.toolbarDropDown) { name = "omni-tool-filter-button" };
            private static Texture2D GenerateSolidColorTexture(Color fillColor)
            {
                Texture2D texture = new Texture2D(1, 1);
                var fillColorArray = texture.GetPixels();

                for (var i = 0; i < fillColorArray.Length; ++i)
                    fillColorArray[i] = fillColor;

                texture.SetPixels(fillColorArray);
                texture.Apply();

                return texture;
            }
        }

        internal struct DebugTimer : IDisposable
        {
            private bool m_Disposed;
            private string m_Name;
            private Stopwatch m_Timer;

            public DebugTimer(string name)
            {
                m_Disposed = false;
                m_Name = name;
                m_Timer = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                m_Timer.Stop();
                TimeSpan timespan = m_Timer.Elapsed;
                Debug.Log($"{m_Name} took {timespan.TotalMilliseconds} ms");
            }
        }

        [UsedImplicitly]
        internal void OnEnable()
        {
            m_OmniSearchBoxFocus = true;
            lastFocusedWindow = s_FocusedWindow;
            titleContent.text = "Search Anything!";
            titleContent.image = OmniIcon.omnitool;

            Debug.Log("Service Filter: " + OmniService.FilterToString());
        }

        [UsedImplicitly]
        internal void OnGUI()
        {
            //using (new DebugTimer($"OnGUI.{Event.current.type}"))
            {
                var context = new OmniContext { searchText = m_SearchText, focusedWindow = lastFocusedWindow };

                HandleKeyboardNavigation();

                DrawToolbar(context);
                DrawItems(context);

                UpdateFocusControlState();
            }
        }

        public void Refresh()
        {
            var context = new OmniContext { searchText = m_SearchText, focusedWindow = lastFocusedWindow };
            m_FilteredItems = OmniService.GetItems(context).ToList();
            Repaint();
        }

        private void UpdateFocusControlState()
        {
            if (m_OmniSearchBoxFocus)
            {
                m_OmniSearchBoxFocus = false;
                GUI.FocusControl("OmniSearchBox");
            }
        }

        private void HandleKeyboardNavigation()
        {
            // TODO: support page down and page up

            var evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                var prev = m_SelectedIndex;
                if (evt.keyCode == KeyCode.DownArrow)
                {
                    m_SelectedIndex = Math.Min(m_SelectedIndex + 1, m_FilteredItems.Count - 1);
                    Event.current.Use();
                }
                else if (evt.keyCode == KeyCode.UpArrow)
                {
                    m_SelectedIndex = Math.Max(0, m_SelectedIndex - 1);
                    Event.current.Use();
                }
                else
                    GUI.FocusControl("OmniSearchBox");

                if (prev != m_SelectedIndex)
                    m_FocusSelectedItem = true;
            }
        }

        private void DrawItems(OmniContext context)
        {
            UpdateScrollAreaOffset();

            m_ScrollPosition = GUILayout.BeginScrollView(m_ScrollPosition);
            {
                var itemCount = m_FilteredItems.Count;
                var availableHeight = position.height - m_ScrollViewOffset.yMax;
                var itemSkipCount = Math.Max(0, (int)(m_ScrollPosition.y / Styles.itemRowHeight));
                var itemDisplayCount = Math.Max(0, Math.Min(itemCount, (int)(availableHeight / Styles.itemRowHeight) + 2));
                var topSpaceSkipped = itemSkipCount * Styles.itemRowHeight;

                //if (topSpaceSkipped > 0)
                    GUILayout.Space(topSpaceSkipped);

                int rowIndex = itemSkipCount;
                foreach (var item in m_FilteredItems.GetRange(itemSkipCount, Math.Min(itemDisplayCount, itemCount - itemSkipCount)))
                    DrawItem(item, context, rowIndex++);

                var bottomSpaceSkipped = (itemCount - rowIndex) * Styles.itemRowHeight;
                //if (bottomSpaceSkipped > 0)
                    GUILayout.Space(bottomSpaceSkipped);

                // Fix selected index display if out of virtual scrolling area
                if (Event.current.type == EventType.Repaint && m_FocusSelectedItem && m_SelectedIndex >= 0)
                {
                    ScrollToItem(itemSkipCount + 1, itemSkipCount + itemDisplayCount - 2, m_SelectedIndex);
                    m_FocusSelectedItem = false;
                }
            }
            GUILayout.EndScrollView();
        }

        private void ScrollToItem(int start, int end, int selection)
        {
            if (start <= selection && selection < end)
                return;

            Rect projectedSelectedItemRect = new Rect(0, selection * Styles.itemRowHeight, position.width, Styles.itemRowHeight);
            if (selection < start)
            {
                m_ScrollPosition.y = projectedSelectedItemRect.y - 2;
                Repaint();
            }
            else if (selection > end)
            {
                Rect visibleRect = GetVisibleRect();
                m_ScrollPosition.y += (projectedSelectedItemRect.yMax - visibleRect.yMax) + 2;
                Repaint();
            }
        }

        private void UpdateScrollAreaOffset()
        {
            var rect = GUILayoutUtility.GetLastRect();
            if (rect.height > 1)
                m_ScrollViewOffset = rect;
        }

        private void DrawToolbar(OmniContext context)
        {
            GUILayout.BeginHorizontal(Styles.toolbar);
            {
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName("OmniSearchBox");
                context.searchText = m_SearchText = EditorGUILayout.TextField(m_SearchText, Styles.searchField);
                if (GUILayout.Button("", Styles.searchFieldClear))
                {
                    m_SelectedIndex = -1;
                    context.searchText = m_SearchText = "";
                    titleContent.text = "Search Anything!";
                }
                
                if (EditorGUI.EndChangeCheck() || m_FilteredItems == null)
                {
                    m_SelectedIndex = -1;
                    m_FilteredItems = OmniService.GetItems(context).ToList();
                    titleContent.text = $"Found {m_FilteredItems.Count} Anything!";
                }

                var rightRect = GUILayoutUtility.GetLastRect();
                if (EditorGUILayout.DropdownButton(Styles.filterButtonContent, FocusType.Passive, Styles.filterButton, GUILayout.MaxWidth(70f)))
                {
                    if (FilterWindow.ShowAtPosition(this, rightRect))
                    {
                        GUIUtility.ExitGUI();
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        private Rect GetVisibleRect()
        {
            Rect visibleRect = position;
            visibleRect.x = m_ScrollPosition.x;
            visibleRect.y = m_ScrollPosition.y;
            visibleRect.height -= m_ScrollViewOffset.yMax;
            return visibleRect;
        }

        private RectVisibility GetRectVisibility(Rect rect, out Rect visibleRect)
        {
            visibleRect = GetVisibleRect();

            if (rect.yMin >= visibleRect.yMin &&
                rect.yMax <= visibleRect.yMax)
                return RectVisibility.Visible;

            if (rect.yMax < visibleRect.yMin)
                return RectVisibility.HiddenTop;
            if (rect.yMin > visibleRect.yMax)
                return RectVisibility.HiddenBottom;
            
            if (rect.yMin < visibleRect.yMin && rect.yMax > visibleRect.yMin)
                return RectVisibility.PartiallyHiddenTop;

            if (rect.yMin < visibleRect.yMax)
                return RectVisibility.PartiallyHiddenBottom;
            
            return RectVisibility.None;
        }

        private void DrawItem(OmniItem item, OmniContext context, int index)
        {
            var bgStyle = index % 2 == 0 ? Styles.itemBackground1 : Styles.itemBackground2;
            if (m_SelectedIndex == index)
                bgStyle = Styles.selectedItemBackground;

            GUILayout.BeginHorizontal(bgStyle);
            {
                GUILayout.Label(item.provider.generatePreview(item, context), Styles.preview);

                GUILayout.BeginVertical();
                {
                    var textMaxWidthLayoutOption = GUILayout.MaxWidth(position.width * 0.7f);
                    GUILayout.Label(item.label ?? item.id, Styles.itemLabel, textMaxWidthLayoutOption);
                    GUILayout.Label(item.description, Styles.itemDescription, textMaxWidthLayoutOption);
                }
                GUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                // TODO: keep current item, draw actions for hovered item and selected item
                foreach (var action in item.provider.actions)
                {
                    if (GUILayout.Button(action.content, Styles.actionButton))
                    {
                        action.handler(item, context);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        [UsedImplicitly, Shortcut("Window/Omni Tool", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void PopOmniToolAll()
        {
            OmniTool.s_FocusedWindow = focusedWindow;
            OmniService.ResetFilter(true);
            GetWindow<OmniTool>();
            FocusWindowIfItsOpen<OmniTool>();
        }

        [UsedImplicitly, Shortcut("Window/Omni Tool (Menu)", KeyCode.M, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void PopOmniToolMenu()
        {
            OmniTool.s_FocusedWindow = focusedWindow;
            OmniService.ResetFilter(false);
            OmniService.SetFilter(true, MenuProvider.type);
            GetWindow<OmniTool>();
            FocusWindowIfItsOpen<OmniTool>();
        }
    }

    namespace Providers
    {
        [UsedImplicitly]
        static class AssetProvider
        {
            internal static string type = "asset";
            internal static string displayName = "Asset";
            /* Filters:
                 t:<type>
                l:<label>
                ref[:id]:path
                v:<versionState>
                s:<softLockState>
                a:<area> [assets, packages]
             */

            [UsedImplicitly, OmniItemProvider]
            internal static OmniProvider CreateProvider()
            {
                var provider = new OmniProvider(type, displayName)
                {
                    fetchItems = (context) =>
                    {
                        var filter = context.searchText;
                        if (context.categories.Any(c => !c.isEnabled))
                        {
                            // Not all categories are enabled, so create a proper filter:
                            filter = string.Join(" ", context.categories.Where(c => c.isEnabled).Select(c => "t:" + c.name.id)) + filter;
                            Debug.Log("Asset filter string: " + filter);
                        }

                        return AssetDatabase.FindAssets(filter).Select(guid =>
                        {
                            var path = AssetDatabase.GUIDToAssetPath(guid);
                            long fileSize = 0;
                            bool isFile = File.Exists(path);
                            if (isFile)
                                fileSize = Math.Max(1024, new FileInfo(path).Length);
                            return new OmniItem
                            {
                                id = path,
                                label = Path.GetFileName(path),
                                description = isFile ? $"{path} ({fileSize / 1024} kb)" : $"{path} (folder)"
                            };
                        });
                    },

                    generatePreview = (item, context) =>
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.id);
                        if (obj != null)
                            return AssetPreview.GetMiniThumbnail(obj);
                        return null;
                    },

                    subCategories = new List<OmniName>()
                };

                foreach (var subCat in new []
                {
                    "AnimationClip",
                    "AudioClip",
                    "AudioMixer",
                    "ComputeShader",
                    "Font",
                    "GUISKin",
                    "Material",
                    "Mesh",
                    "Model",
                    "PhysicMaterial",
                    "Prefab",
                    "Scene",
                    "Script",
                    "Shader",
                    "Sprite",
                    "Texture",
                    "VideoClip"
                })
                {
                    provider.subCategories.Add(new OmniName(subCat));
                }

                return provider;
            }

            [UsedImplicitly, OmniActionsProvider]
            internal static IEnumerable<OmniAction> ActionHandlers()
            {
                // Select
                // Open
                // Show in Explorer
                // Copy path
                return new[]
                {
                new OmniAction("asset", "select", OmniIcon.@goto, "Select asset...") { handler = (item, context) =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.id);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }},
                new OmniAction("asset", "open", OmniIcon.open, "Open asset...") { handler = (item, context) =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.id);
                    if (asset != null)
                    {
                        AssetDatabase.OpenAsset(asset);
                    }
                }}
            };
            }
        }

        [UsedImplicitly]
        static class MenuProvider
        {
            internal static string type = "menu";
            internal static string displayName = "Menu";
            [UsedImplicitly, OmniItemProvider]
            internal static OmniProvider CreateProvider()
            {
                return new OmniProvider(type, displayName)
                {
                    fetchItems = (context) =>
                    {
                        var itemNames = new List<string>();
                        var shortcuts = new List<string>();
                        GetMenuInfo(itemNames, shortcuts);

                        return itemNames.Where(menuName => OmniProvider.MatchSearchGroups(context.searchText, menuName)).Select(menuName => new OmniItem
                        {
                            id = menuName,
                            description = menuName,
                            label = Path.GetFileName(menuName)
                        });
                    },

                    generatePreview = (item, context) => OmniIcon.shortcut
                };
            }

            [UsedImplicitly, OmniActionsProvider]
            internal static IEnumerable<OmniAction> ActionHandlers()
            {
                // Select
                // Open
                // Show in Explorer
                // Copy path
                return new[]
                {
                    new OmniAction("menu", "exec", OmniIcon.execute, "Execute shortcut...") { handler = (item, context) =>
                    {
                        EditorApplication.ExecuteMenuItem(item.id);
                    }}
                };
            }

            private static void GetMenuInfo(List<string> outItemNames, List<string> outItemDefaultShortcuts)
            {
                Assembly assembly = typeof(Menu).Assembly;
                var managerType = assembly.GetTypes().First(t => t.Name == "Menu");
                var method = managerType.GetMethod("GetMenuItemDefaultShortcuts", BindingFlags.NonPublic | BindingFlags.Static);
                var arguments = new object[] { outItemNames, outItemDefaultShortcuts };
                method.Invoke(null, arguments);
            }
        }
    }
}