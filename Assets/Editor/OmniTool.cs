using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public int instanceID;
        public OmniProvider provider;
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

        public String type;
        public PreviewHandler generatePreview;
        public GetItemsHandler fetchItems;
        public List<OmniAction> actions;
    }

    public struct OmniContext
    {
        public string searchText;
        public EditorWindow focusedWindow;
    }

    public class OmniItemProviderAttribute : Attribute
    {
    }

    public class OmniActionsProviderAttribute : Attribute
    {
    }

    public static class OmniService
    {
        static List<OmniProvider> s_Providers;
        public static List<OmniProvider> Providers
        {
            get
            {
                if (s_Providers == null)
                {
                    FetchProviders();
                }

                return s_Providers;
            }
        }

        public static void FetchProviders()
        {
            s_Providers = GetAllMethodsWithAttribute(typeof(OmniItemProviderAttribute)).Select(methodInfo => methodInfo.Invoke(null, null) as Omni.OmniProvider).Where(provider => provider != null).ToList();

            foreach (var action in GetAllMethodsWithAttribute(typeof(OmniActionsProviderAttribute)).SelectMany(methodInfo => methodInfo.Invoke(null, null) as object[]).Where(a => a != null).Cast<OmniAction>())
            {
                var provider = s_Providers.Find(p => p.type == action.type);
                provider?.actions.Add(action);
            }
        }

        public static IEnumerable<OmniItem> GetItems(OmniContext context)
        {
            return Providers.SelectMany(provider => provider.fetchItems(context).Select(item =>
            {
                item.provider = provider;
                return item;
            }));
        }

        private static IEnumerable<MethodInfo> GetAllMethodsWithAttribute(System.Type attrType, BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        {
            Assembly assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "EditorAssemblies");
            var method = managerType.GetMethod("Internal_GetAllMethodsWithAttribute", BindingFlags.NonPublic | BindingFlags.Static);
            var arguments = new object[] { attrType, bindingFlags };
            return (method.Invoke(null, arguments) as object[]).Cast<MethodInfo>();
        }
    }

    public class OmniTool : EditorWindow
    {
        static EditorWindow s_FocusedWindow;

        [SerializeField]
        EditorWindow m_FocusedWindow;

        [SerializeField]
        string m_SearchText;

        [SerializeField]
        Vector2 m_Scroll;

        IEnumerable<OmniItem> m_FilteredItems;

        // public override void OnGUI(Rect rect)
        public void OnGUI()
        {
            var context = new OmniContext() { searchText = m_SearchText, focusedWindow = m_FocusedWindow };
            EditorGUI.BeginChangeCheck();
            m_SearchText = EditorGUILayout.TextField(m_SearchText);
            if (EditorGUI.EndChangeCheck() || m_FilteredItems == null)
            {
                m_FilteredItems = OmniService.GetItems(context);
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

            // TODO: virtual scroll -> either use GUI.Space or set the height of the scroll area
            m_Scroll = GUILayout.BeginScrollView(m_Scroll);

            foreach (var item in m_FilteredItems.Take(10))
            {
                // TODO: virtual scroll according to scroll pos
                // TODO: precompute rects
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(item.provider.generatePreview(item, context));

                    {
                        GUILayout.BeginVertical();
                        GUILayout.Label(item.label ?? item.id);
                        GUILayout.Label(item.description);
                        GUILayout.EndVertical();
                    }

                    GUILayout.FlexibleSpace();

                    // TODO: keep current item, draw actions for hovered item and selected item
                    foreach (var action in item.provider.actions)
                    {
                        if (GUILayout.Button(action.content))
                        {
                            action.handler(item, context);
                            GUIUtility.ExitGUI();
                        }
                    }

                    GUILayout.EndHorizontal();
                }
                
            }
            GUILayout.EndScrollView();
        }

        public void OnEnable()
        {
            m_FocusedWindow = s_FocusedWindow;
        }

        /*
        public override Vector2 GetWindowSize()
        {
            // TODO: persist size and modify size according to if the list is popped or not
            return new Vector2(750, 600f);
        }

        public override void OnOpen()
        {
            OnEnable();
        }

        public override void OnClose()
        {
        }
        */

        [Shortcut("Window/Omni Tool", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void PopOmniTool()
        {
            s_FocusedWindow = EditorWindow.focusedWindow;
            GetWindow<OmniTool>();
            EditorWindow.FocusWindowIfItsOpen<OmniTool>();

            /*
            try
            {
                
                // TODO: center on screen
                PopupWindow.Show(new Rect(100, 100, 400, 800), new OmniTool());
            }
            catch (Exception )
            {
                
            }
            */
        }
    }


    namespace Providers
    {
        static class AssetProvider
        {
            [OmniItemProvider]
            static OmniProvider CreateProvider()
            {
                return new OmniProvider("asset")
                {
                    fetchItems = (context) =>
                    {
                        return AssetDatabase.FindAssets(context.searchText).Select(guid =>
                        {
                            var path = AssetDatabase.GUIDToAssetPath(guid);
                            return new OmniItem()
                            {
                                id = path,
                                label = Path.GetFileName(path),
                                description = path
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

            [OmniActionsProvider]
            static IEnumerable<OmniAction> ActionHandlers()
            {
                // Select
                // Open
                // Show in Explorer
                // Copy path
                return new[]
                {
                new OmniAction("asset", "select") { handler = (item, context) =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.id);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }},
                new OmniAction("asset", "open") { handler = (item, context) =>
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

        static class MenuProvider
        {
            [OmniItemProvider]
            static Omni.OmniProvider CreateProvider()
            {
                return new OmniProvider("menu")
                {
                    fetchItems = (context) =>
                    {
                        var itemNames = new List<string>();
                        var shortcuts = new List<string>();
                        GetMenuInfo(itemNames, shortcuts);

                        return itemNames.Select(menuName => new OmniItem()
                        {
                            id = menuName,
                            description = menuName,
                            label = Path.GetFileName(menuName)
                        });
                    }
                };
            }

            [OmniActionsProvider]
            static IEnumerable<OmniAction> ActionHandlers()
            {
                // Select
                // Open
                // Show in Explorer
                // Copy path
                return new[]
                {
                    new OmniAction("menu", "exec") { handler = (item, context) =>
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