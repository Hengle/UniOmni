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
        public OmniFilter()
        {
            filteredProviderIds = new List<string>();
            filteredProviders = new List<OmniProvider>();
            providerFilters = new List<HashSet<string>>();
        }

        [SerializeField]
        public List<string> filteredProviderIds;
        public List<OmniProvider> filteredProviders;
        public List<HashSet<string>> providerFilters;
    }

    public class OmniProvider
    {
        public OmniProvider(string type)
        {
            this.type = type;
            actions = new List<OmniAction>();
            fetchItems = (context) => new OmniItem[0];
            generatePreview = (item, context) => null;
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

        public String type;
        public PreviewHandler generatePreview;
        public GetItemsHandler fetchItems;
        public List<OmniAction> actions;
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
            var filterStr = EditorPrefs.GetString(k_KFilterPrefKey, "all");
            if (filterStr == "all")
            {
                ResetFilter();
            }
            else
            {
                var filters = filterStr.Split(',');
                SetFilteredProviders(filters);
            }
        }

        public static OmniFilter Filter { get; private set; }
        
        public static IEnumerable<OmniItem> GetItems(OmniContext context)
        {
            return Filter.filteredProviders.SelectMany(provider =>
            {
                context.currentProviderFilters = Filter.providerFilters[Filter.filteredProviders.FindIndex(p => p == provider)];
                return provider.fetchItems(context).Select(item =>
                {
                    item.provider = provider;
                    return item;
                });
            });
        }

        public static void ResetFilter(bool selectAll = true)
        {
            if (selectAll)
            {
                Filter.filteredProviderIds = new List<string>(Providers.Select(p => p.type));
                Filter.filteredProviders = new List<OmniProvider>(Providers);
            }
            else
            {
                Filter.filteredProviderIds = new List<string>();
                Filter.filteredProviders = new List<OmniProvider>();
            }
            SaveFilters();
        }

        public static void SetFilteredProviders(IEnumerable<string> providerIds)
        {
            Filter.filteredProviderIds = new List<string>(providerIds);
            Filter.filteredProviders = Providers.Where(p => providerIds.Contains(p.type)).ToList();
            // TODO populate
            Filter.providerFilters = Filter.filteredProviders.Select(dummy => new HashSet<string>()).ToList();
            SaveFilters();
        }

        public static void UpdateFilter(string filterPath, bool isEnabled)
        {
            // TODO handle subfilter
            if (isEnabled)
            {
                Filter.filteredProviderIds.Add(filterPath);
            }
            else
            {
                Filter.filteredProviderIds.Remove(filterPath);
            }

            Filter.filteredProviders = Providers.Where(p => Filter.filteredProviderIds.Contains(p.type)).ToList();
            // TODO populate
            Filter.providerFilters = Filter.filteredProviders.Select(dummy => new HashSet<string>()).ToList();
            SaveFilters();
        }

        private static void FetchProviders()
        {
            Providers = GetAllMethodsWithAttribute<OmniItemProviderAttribute>()
                .Select(methodInfo => methodInfo.Invoke(null, null) as OmniProvider)
                .Where(provider => provider != null).ToList();

            foreach (var action in GetAllMethodsWithAttribute<OmniActionsProviderAttribute>()
                .SelectMany(methodInfo => methodInfo.Invoke(null, null) as object[]).Where(a => a != null).Cast<OmniAction>())
            {
                var provider = Providers.Find(p => p.type == action.type);
                provider?.actions.Add(action);
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

        private static void SaveFilters()
        {
            var filterStr = string.Join(",", Filter.filteredProviderIds);
            EditorPrefs.SetString(k_KFilterPrefKey, filterStr);
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
        private IEnumerable<OmniItem> m_FilteredItems;

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
            private static readonly Texture2D buttonPressedBackgroundImage = GenerateSolidColorTexture(new Color(111/255f, 111/255f, 111/255f));
            private static readonly Texture2D buttonHoveredBackgroundImage = GenerateSolidColorTexture(new Color(71/255f, 71/255f, 71/255f));

            public static readonly GUIStyle itemBackground1 = new GUIStyle
            {
                name = "omni-tool-item-background1",
                fixedHeight = itemRowHeight,

                margin = marginNone,
                padding = defaultPadding
            };

            public static readonly GUIStyle itemBackground2 = new GUIStyle
            {
                name = "omni-tool-item-background2",
                fixedHeight = itemRowHeight,

                margin = marginNone,
                padding = defaultPadding,

                normal = new GUIStyleState { background = alternateRowBackgroundImage, scaledBackgrounds = new[] { alternateRowBackgroundImage } }
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

                margin = new RectOffset(4, 4, 7, 2),
                padding = paddingNone
            };

            public static readonly GUIStyle itemDescription = new GUIStyle(EditorStyles.label)
            {
                name = "omni-tool-item-description",

                margin = new RectOffset(4, 4, 1, 4),
                padding = paddingNone,

                fontSize = itemLabel.fontSize - 3,
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

            public static readonly GUIStyle searchField = new GUIStyle(EditorStyles.toolbarSearchField)
            {
                name = "omni-tool-search-field"
            };

            public static readonly GUIStyle searchFieldClear = new GUIStyle("ToolbarSeachCancelButton")
            {
                name = "omni-tool-search-field-clear"
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
            lastFocusedWindow = s_FocusedWindow;
            titleContent.text = "Search Anything!";
            titleContent.image = EditorGUIUtility.IconContent("winbtn_mac_max").image;
        }

        [UsedImplicitly]
        internal void OnGUI()
        {
            var context = new OmniContext { searchText = m_SearchText, focusedWindow = lastFocusedWindow };

            DrawToolbar(context);
            DrawItems(context);
        }

        private void DrawItems(OmniContext context)
        {
            // TODO: virtual scroll -> either use GUI.Space or set the height of the scroll area
            m_ScrollPosition = GUILayout.BeginScrollView(m_ScrollPosition);

            int rowIndex = 0;
            foreach (var item in m_FilteredItems.Take(10000))
                DrawItem(item, context, rowIndex++);
            GUILayout.EndScrollView();
        }

        private void DrawToolbar(OmniContext context)
        {
            GUILayout.BeginHorizontal(GUI.skin.FindStyle("Toolbar"));
            EditorGUI.BeginChangeCheck();
            m_SearchText = EditorGUILayout.TextField(m_SearchText, Styles.searchField);
            if (GUILayout.Button("", Styles.searchFieldClear))
            {
                // Remove focus if cleared
                m_SearchText = "";
                GUI.FocusControl(null);
            }
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
            GUILayout.EndHorizontal();
        }

        private void DrawItem(OmniItem item, OmniContext context, int index)
        {
            // TODO: virtual scroll according to scroll pos
            // TODO: precompute rects

            GUILayout.BeginHorizontal(index % 2 == 0 ? Styles.itemBackground1 : Styles.itemBackground2);
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
            GetWindow<OmniTool>();
            FocusWindowIfItsOpen<OmniTool>();
        }

        [UsedImplicitly, Shortcut("Window/Omni Tool (Menu)", KeyCode.M, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void PopOmniToolMenu()
        {
            OmniTool.s_FocusedWindow = focusedWindow;
            OmniService.SetFilteredProviders(new [] { MenuProvider.type });
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
                return new OmniProvider(type)
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
            [UsedImplicitly, OmniItemProvider]
            internal static OmniProvider CreateProvider()
            {
                return new OmniProvider(type)
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