using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Omni.Providers;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

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
                this.entry = new Entry(name);
                categories = new List<Entry>();
            }

            public Entry entry;
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

        public HashSet<string> GetEnabledSubCategories(OmniProvider provider)
        {
            var desc = model.Find(pd => pd.entry.name.id == provider.name.id);
            if (desc == null)
            {
                return new HashSet<string>();
            }

            return new HashSet<string>(desc.categories.Where(c => c.isEnabled).Select(c => c.name.id));
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
            this.name = new OmniName(id, displayName);
            actions = new List<OmniAction>();
            fetchItems = (context) => new OmniItem[0];
            generatePreview = (item, context) => null;
            subCategories = new List<OmniName>();
        }

        public static bool MatchSearchGroups(string searchContext, string content)
        {
            int dummyStart;
            int dummyEnd;
            return MatchSearchGroups(searchContext, content, out dummyStart, out dummyEnd);
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
        public HashSet<string> currentProviderFilters;
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
        public static List<OmniProvider> Providers { get; private set; }

        static OmniService()
        {
            Filter = new OmniFilter();
            FetchProviders();
            var filterStr = EditorPrefs.GetString(k_KFilterPrefKey, null);
            if (filterStr == null)
            {
                ResetFilter(true);
            }
            else
            {
                SetFilters(filterStr);
            }
        }

        public static OmniFilter Filter { get; private set; }
        
        public static IEnumerable<OmniItem> GetItems(OmniContext context)
        {
            return Filter.filteredProviders.SelectMany(provider =>
            {
                context.currentProviderFilters = Filter.GetEnabledSubCategories(provider);
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
            UpdateFilteredProviders();
            SaveFilters();
        }

        public static void SetFilter(bool isEnabled, string providerId, string subCategory = null)
        {
            if (SetFilterInternal(isEnabled, providerId, subCategory))
            {
                UpdateFilteredProviders();
                SaveFilters();
            }
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

        private static void UpdateFilteredProviders()
        {
            Filter.filteredProviders = Providers.Where(p => Filter.IsEnabled(p.name.id)).ToList();
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

        private static void SetFilters(string filtersStr)
        {
            var filters = filtersStr.Split(',');
            foreach (var filterDesc in filters)
            {
                var filter = filterDesc.Split(':');
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

        private static void SaveFilters()
        {
            // var filterStr = string.Join(",", Filter.filteredProviderIds);
            // EditorPrefs.SetString(k_KFilterPrefKey, filterStr);
        }
    }

    internal class FilterWindow : EditorWindow
    {
        public static bool ShowAtPosition(Rect rect, Vector2 windowSize)
        {
            var screenRect = GUIUtility.GUIToScreenRect(rect);
            var filterWindow = ScriptableObject.CreateInstance<FilterWindow>();
            filterWindow.ShowAsDropDown(screenRect, windowSize);
            return true;
        }

        void OnGUI()
        {

        }
    }

    static class OmniIcon
    {
        public static Texture2D open = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/open.png");
        public static Texture2D @goto = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/goto.png");
        public static Texture2D shortcut = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/shortcut.png");
        public static Texture2D execute = EditorGUIUtility.FindTexture("Packages/com.unity.omnitool/Editor/Icons/execute.png");

        static OmniIcon()
        {
            bool hasPro = UnityEditorInternal.InternalEditorUtility.HasPro();
            if (hasPro)
            {
                open = LightenTexture(open);
                @goto = LightenTexture(@goto);
                shortcut = LightenTexture(shortcut);
                execute = LightenTexture(execute);
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
        private IEnumerable<OmniItem> m_FilteredItems;
        private bool m_FocusSelectedItem = false;
        private Rect m_ScrollViewOffset;

        static class Styles
        {
            static Styles()
            {
            }
        
            private const int itemRowPadding = 4;
            private const float actionButtonSize = 24f;
            private const float itemPreviewSize = 32f;
            private const float itemRowHeight = itemPreviewSize + itemRowPadding * 2f;
            private const int actionButtonMargin = (int)((itemRowHeight - actionButtonSize) / 2f);

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
                name = "omni-tool-bar"
            };
                

            public static readonly GUIStyle searchField = new GUIStyle(EditorStyles.toolbarSearchField)
            {
                name = "omni-tool-search-field"
            };

            public static readonly GUIStyle searchFieldClear = new GUIStyle("ToolbarSeachCancelButton")
            {
                name = "omni-tool-search-field-clear"
            };

            public static readonly GUIStyle filterButton = new GUIStyle("ToolBarDropDown")
            {
                name = "omni-tool-filter-button"
            };

            public static readonly GUIStyle filterHeader = new GUIStyle("BoldLabel")
            {
                name = "omni-tool-filter-header"
            };

            public static readonly GUIStyle filterEntry = new GUIStyle("label")
            {
                name = "omni-tool-filter-entry"
            };

            public static readonly GUIStyle filterToggle = new GUIStyle("OL Toggle")
            {
                name = "omni-tool-filter-toggle"
            };

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

        [UsedImplicitly]
        internal void OnEnable()
        {
            m_OmniSearchBoxFocus = true;
            lastFocusedWindow = s_FocusedWindow;
            titleContent.text = "Search Anything!";
            titleContent.image = EditorGUIUtility.IconContent("winbtn_mac_max").image;
        }

        [UsedImplicitly]
        internal void OnGUI()
        {
            var context = new OmniContext { searchText = m_SearchText, focusedWindow = lastFocusedWindow };

            HandleKeyboardNavigation();

            DrawToolbar(context);
            DrawItems(context);

            UpdateFocusControlState();
        }

        private void UpdateFocusControlState()
        {
            if (m_OmniSearchBoxFocus)
            {
                m_OmniSearchBoxFocus = false;
                GUI.FocusControl("OmniSearchBox");
            }
            else if (m_FocusSelectedItem)
            {
                GUI.FocusControl("Item" + m_SelectedIndex);
                m_FocusSelectedItem = false;
            }
        }

        private void HandleKeyboardNavigation()
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                var prev = m_SelectedIndex;
                if (evt.keyCode == KeyCode.DownArrow)
                    m_SelectedIndex = Math.Min(m_SelectedIndex + 1, m_FilteredItems.Count() - 1);
                else if (evt.keyCode == KeyCode.UpArrow)
                    m_SelectedIndex = Math.Max(0, m_SelectedIndex - 1);
                else
                    GUI.FocusControl("OmniSearchBox");

                if (prev != m_SelectedIndex)
                {
                    m_FocusSelectedItem = true;
                    Event.current.Use();
                }
            }
        }

        private void DrawItems(OmniContext context)
        {
            m_ScrollViewOffset = GUILayoutUtility.GetLastRect();

            // TODO: virtual scroll -> either use GUI.Space or set the height of the scroll area
            m_ScrollPosition = GUILayout.BeginScrollView(m_ScrollPosition);

            int rowIndex = 0;
            foreach (var item in m_FilteredItems.Take(10))
                DrawItem(item, context, rowIndex++);
            GUILayout.EndScrollView();
        }

        private void DrawToolbar(OmniContext context)
        {
            GUILayout.BeginHorizontal(Styles.toolbar);
            {
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName("OmniSearchBox");
                context.searchText = m_SearchText = EditorGUILayout.TextField(m_SearchText, Styles.searchField);
                if (GUILayout.Button("", Styles.searchFieldClear))
                    context.searchText = m_SearchText = "";
                
                if (EditorGUI.EndChangeCheck() || m_FilteredItems == null)
                {
                	m_FilteredItems = OmniService.GetItems(context).ToList();
                    /*
                    if (string.IsNullOrEmpty(m_SearchText))
                    {
                        m_FilteredItems = new Item[0];
                    }
                    else
                    {
                        m_FilteredItems = OmniService.GetItems(context);
                    }
                    */
                }
            }

            Rect r = GUILayoutUtility.GetRect(Styles.filterButtonContent, Styles.filterButton);
            Rect rightRect = new Rect(r.xMin, r.y, r.xMax, r.height);
            if (EditorGUI.DropdownButton(rightRect, Styles.filterButtonContent, FocusType.Passive, GUIStyle.none))
            {
                if (FilterWindow.ShowAtPosition(rightRect, new Vector2(100, 200)))
                {
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawItem(OmniItem item, OmniContext context, int index)
        {
            // TODO: virtual scroll according to scroll pos
            // TODO: precompute rects

            var bgStyle = index % 2 == 0 ? Styles.itemBackground1 : Styles.itemBackground2;
            if (m_SelectedIndex == index)
                bgStyle = Styles.selectedItemBackground;

            GUILayout.BeginHorizontal(bgStyle);
            {
                GUILayout.Label(item.provider.generatePreview(item, context), Styles.preview);

                if (m_SelectedIndex == index)
                {
                    var rect = GUILayoutUtility.GetLastRect();
                    if (rect.height > 1)
                    {
                        if (Event.current.type == EventType.Repaint)
                        {
                            Rect visibleRect = position;
                            visibleRect.x = m_ScrollPosition.x;
                            visibleRect.y = m_ScrollPosition.y - m_ScrollViewOffset.yMax + 1;
                            var topLeft = new Vector2(rect.x, rect.y);
                            var bottomRight = new Vector2(rect.xMax, rect.yMax);

                            if (topLeft.y < visibleRect.yMin)
                            {
                                m_ScrollPosition.y = topLeft.y - 2;
                                Repaint();
                            }
                            else if (bottomRight.y > visibleRect.yMax)
                            {
                                m_ScrollPosition.y += (bottomRight.y - visibleRect.yMax) + 2;
                                Repaint();
                            }
                        }
                    }
                }

                GUILayout.BeginVertical();
                {
                    var textMaxWidthLayoutOption = GUILayout.MaxWidth(position.width * 0.7f);
                    GUILayout.Label(item.label ?? item.id, Styles.itemLabel, textMaxWidthLayoutOption);
                    GUILayout.Label(item.description, Styles.itemDescription, textMaxWidthLayoutOption);
                }
                GUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                // TODO: keep current item, draw actions for hovered item and selected item
                GUI.SetNextControlName("Item" + index);
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
                 AnimationClip
                 AudioClip
                 AudioMixer
                 ComputeShader
                 Font
                 GUISKin
                 Material
                 Mesh
                 Model
                 PhysicMaterial
                 PRefab
                 Scene
                 Script
                 Shader
                 Sprite
                 Texture
                 VideoClip

                l:<label>
                ref[:id]:path
                v:<versionState>
                s:<softLockState>
                a:<area> [assets, packages]
             */

            [UsedImplicitly, OmniItemProvider]
            internal static OmniProvider CreateProvider()
            {
                return new OmniProvider(type, displayName)
                {
                    fetchItems = (context) =>
                    {
                        return AssetDatabase.FindAssets(context.searchText).Select(guid =>
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
                    }
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