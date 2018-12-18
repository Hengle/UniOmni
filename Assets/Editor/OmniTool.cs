using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Omni
{
    public class OmniAction
    {
        // icon
        // handler
        // action name + tooltip

        public virtual bool IsEnabled(Item item)
        {
            return true;
        }
        public virtual void Execute(Item item)
        {

        }
    }

    public class Item
    {
        public string Name { get; set; }
        public OmniAction[] Actions { get; internal set; }
    }

    /** Potential types of provider:
        
        Menu items (with subcatagory?)
            Settings
            Preferences
            Windows
            Actions
            
        Assets (if it possible to reuse part of the assetdatabase search feature?)
        Scene objects

        API URL
        Tutorial?

        Asset Store items?


    */
    public abstract class Provider
    {
        public abstract List<Item> GetItems();

        public void Execute(Item item, OmniAction action)
        {
            action.Execute(item);
        }
    }

    // Populated async and always available
    public class OmniItemsDb
    {
        public Provider[] Providers { get; private set; }

        // History

        // Favorites?

    }

    /**
        Flat list with action bar
        
        filter according to provider type and subcategory (tree of filter switches)
        Assets
            CSharp
            Textures
        Menu
            Window
            Settings
            Preferences
        GameObject
            tags
                player...
            layer
                background

        History panel
            list of recently searched terms

        Favorites
            Could potentially want boxes/folders of favorites
            would allow a user to reorganize its preferred menu item(s)?
            Create selection group?

     */
    public class OmniTool : PopupWindowContent
    {
        public override void OnGUI(Rect rect)
        {
            
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(750, 600f);
        }

        public override void OnOpen()
        {
        }

        public override void OnClose()
        {
        }

        [Shortcut("Window/Omni Tool", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void PopOmniTool()
        {
            PopupWindow.Show(new Rect(100, 100, 400, 800), new OmniTool());
        }
    }
}

